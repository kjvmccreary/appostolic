import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AgentForm, { type ToolItem, type AgentDetails, validate } from './AgentForm';
import { http, HttpResponse } from 'msw';

// Access the MSW server from global (exposed in test/setup.ts)
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const server = (globalThis as any).__mswServer as import('msw/node').SetupServer;

// Mock next/navigation router
const push = vi.fn();
const refresh = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push, refresh }),
}));

const tools: ToolItem[] = [
  { name: 'web.search', description: 'Search the web', category: 'web' },
  { name: 'fs.write', description: 'Write to a file', category: 'fs' },
];

beforeEach(() => {
  push.mockReset();
  refresh.mockReset();
});

describe('AgentForm - create', () => {
  it('renders fields and tools', () => {
    render(<AgentForm mode="create" tools={tools} />);
    expect(screen.getByLabelText('Name')).toBeInTheDocument();
    expect(screen.getByLabelText('Model')).toBeInTheDocument();
    expect(screen.getByLabelText('System Prompt')).toBeInTheDocument();
    // Tool checkboxes labelled by tool name
    expect(screen.getByRole('checkbox', { name: /web.search/i })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /fs.write/i })).toBeInTheDocument();
  });

  it('shows validation errors for invalid inputs', async () => {
    render(<AgentForm mode="create" tools={tools} />);
    // Empty name
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();

    // Unit-test the validation helper for range errors
    const errsHighTemp = validate({
      name: 'ok',
      model: 'm',
      temperature: 2.1,
      maxSteps: 10,
      systemPrompt: '',
      toolAllowlist: [],
    });
    expect(errsHighTemp.temperature).toBe('Range 0–2');

    const errsMaxSteps = validate({
      name: 'ok',
      model: 'm',
      temperature: 0.5,
      maxSteps: 0,
      systemPrompt: '',
      toolAllowlist: [],
    });
    expect(errsMaxSteps.maxSteps).toBe('Range 1–50');
  });

  it('updates token estimate as prompt changes', async () => {
    render(<AgentForm mode="create" tools={tools} />);
    const prompt = screen.getByLabelText('System Prompt');
    await userEvent.type(prompt, 'hello world'); // 11 chars => ceil(11/4)=3
    expect(screen.getByText(/~3 tokens/)).toBeInTheDocument();
  });

  it('toggles tool checkboxes and sends toolAllowlist on submit', async () => {
    // Capture request bodies
    const bodies: Array<Record<string, unknown>> = [];
    server.use(
      http.post('http://localhost/api-proxy/agents', async ({ request }) => {
        bodies.push((await request.json()) as Record<string, unknown>);
        return HttpResponse.json({ id: 'new-id' }, { status: 201 });
      }),
    );

    render(<AgentForm mode="create" tools={tools} />);
    await userEvent.type(screen.getByLabelText('Name'), 'My Agent');
    // Toggle one tool
    await userEvent.click(screen.getByRole('checkbox', { name: /web.search/i }));
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(push).toHaveBeenCalledWith('/studio/agents'));
    expect(refresh).toHaveBeenCalled();

    const sent = bodies.at(-1);
    expect(sent).toMatchObject({
      name: 'My Agent',
      toolAllowlist: ['web.search'],
    });
  });

  it('sends isEnabled=false when toggled off before submit', async () => {
    const bodies: Array<Record<string, unknown>> = [];
    server.use(
      http.post('http://localhost/api-proxy/agents', async ({ request }) => {
        bodies.push((await request.json()) as Record<string, unknown>);
        return HttpResponse.json({ id: 'created' }, { status: 201 });
      }),
    );

    render(<AgentForm mode="create" tools={tools} />);
    await userEvent.type(screen.getByLabelText('Name'), 'New Agent');
    // Toggle Enabled off
    await userEvent.click(screen.getByRole('checkbox', { name: 'Enabled' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(push).toHaveBeenCalledWith('/studio/agents'));
    const sent = bodies.at(-1);
    expect(sent).toMatchObject({ isEnabled: false });
  });
});

describe('AgentForm - edit', () => {
  it('submits PUT to the correct URL', async () => {
    const initial: Partial<AgentDetails> = {
      id: '123',
      name: 'Old Name',
      model: 'gpt-4o-mini',
      temperature: 0.7,
      maxSteps: 10,
      systemPrompt: 'You are helpful.',
      toolAllowlist: [],
    };

    const seen: { url?: string; method?: string } = {};
    server.use(
      http.put('http://localhost/api-proxy/agents/123', async ({ request }) => {
        seen.url = request.url;
        seen.method = request.method;
        return HttpResponse.json({ ok: true }, { status: 200 });
      }),
    );

    render(<AgentForm mode="edit" initial={initial} tools={tools} />);
    await userEvent.clear(screen.getByLabelText('Name'));
    await userEvent.type(screen.getByLabelText('Name'), 'New Name');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(push).toHaveBeenCalledWith('/studio/agents'));
    expect(seen.method).toBe('PUT');
    // Ensure it targeted the id-specific route
    expect(seen.url).toContain('/api-proxy/agents/123');
  });
});
