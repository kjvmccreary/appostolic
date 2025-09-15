'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';

export type ToolItem = { name: string; description: string; category: string };
export type AgentUpsert = {
  name: string;
  model: string;
  temperature: number;
  maxSteps: number;
  systemPrompt: string;
  toolAllowlist: string[];
  isEnabled?: boolean;
};

export type AgentDetails = AgentUpsert & {
  id: string;
  createdAt: string;
  updatedAt?: string | null;
};

export function estimateTokens(text: string) {
  const chars = text?.length ?? 0;
  return Math.ceil(chars / 4);
}

export function impliesTools(prompt: string) {
  // naive signal: mentions search/db/file
  return /search|browse|web|db|query|sql|file|write|save/i.test(prompt ?? '');
}

export function validate(a: AgentUpsert) {
  const errors: Partial<Record<keyof AgentUpsert, string>> = {};
  if (!a.name || a.name.trim() === '') errors.name = 'Name is required';
  if (a.name && a.name.length > 120) errors.name = 'Max 120 chars';
  if (a.model && a.model.length > 80) errors.model = 'Max 80 chars';
  if (a.temperature < 0 || a.temperature > 2) errors.temperature = 'Range 0–2';
  if (a.maxSteps < 1 || a.maxSteps > 50) errors.maxSteps = 'Range 1–50';
  return errors;
}

type Props = {
  mode: 'create' | 'edit';
  initial?: Partial<AgentDetails>;
  tools: ToolItem[];
};

// Agent form with inline validation, ARIA helper texts, and tool allowlist hints.
export default function AgentForm({ mode, initial, tools }: Props) {
  const router = useRouter();
  const [form, setForm] = useState<AgentUpsert>({
    name: initial?.name ?? '',
    model: initial?.model ?? 'gpt-4o-mini',
    temperature: initial?.temperature ?? 0.7,
    maxSteps: initial?.maxSteps ?? 12,
    systemPrompt: initial?.systemPrompt ?? '',
    toolAllowlist: initial?.toolAllowlist ?? [],
    isEnabled: initial?.isEnabled ?? true,
  });
  const [errors, setErrors] = useState<Partial<Record<keyof AgentUpsert, string>>>({});
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<string | null>(null);

  const tokenEstimate = useMemo(() => estimateTokens(form.systemPrompt), [form.systemPrompt]);
  const toolWarning = useMemo(
    () => form.toolAllowlist.length === 0 && impliesTools(form.systemPrompt),
    [form.toolAllowlist, form.systemPrompt],
  );

  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 2000);
    return () => clearTimeout(t);
  }, [toast]);

  function setField<K extends keyof AgentUpsert>(key: K, value: AgentUpsert[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function onToggleTool(name: string) {
    setForm((f) => ({
      ...f,
      toolAllowlist: f.toolAllowlist.includes(name)
        ? f.toolAllowlist.filter((x) => x !== name)
        : [...f.toolAllowlist, name],
    }));
  }

  async function onSubmit() {
    const v = validate(form);
    setErrors(v);
    if (Object.keys(v).length) return;

    setSaving(true);
    try {
      const body = JSON.stringify({
        name: form.name.trim(),
        model: form.model.trim(),
        temperature: form.temperature,
        maxSteps: form.maxSteps,
        systemPrompt: form.systemPrompt,
        toolAllowlist: form.toolAllowlist,
        isEnabled: form.isEnabled,
      });

      const isEdit = mode === 'edit' && initial?.id;
      const url = isEdit ? `/api-proxy/agents/${initial!.id}` : '/api-proxy/agents';
      const method = isEdit ? 'PUT' : 'POST';
      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body,
      });
      if (!res.ok) {
        setToast('Failed to save');
        setSaving(false);
        return;
      }

      // Navigate back to agents list and refresh.
      router.push('/studio/agents');
      router.refresh();
    } catch {
      setToast('Failed to save');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      {toast && (
        <div
          role="status"
          className="rounded bg-red-50 border border-red-200 text-red-700 px-3 py-2 text-sm"
        >
          {toast}
        </div>
      )}

      <div className="grid md:grid-cols-2 gap-6">
        <div className="space-y-4">
          <div>
            <label htmlFor="agent-name" className="block text-sm font-medium">
              Name
            </label>
            <input
              className="mt-1 w-full rounded border px-3 py-2 text-sm"
              id="agent-name"
              value={form.name}
              onChange={(e) => setField('name', e.target.value)}
              maxLength={120}
              placeholder="My Study Buddy"
              title="Agent name (required, up to 120 characters)"
              {...(errors.name ? { 'aria-invalid': 'true' } : {})}
              aria-describedby={`agent-name-help${errors.name ? ' agent-name-error' : ''}`}
            />
            <p id="agent-name-help" className="text-xs text-gray-600 mt-1">
              Required; up to 120 characters.
            </p>
            {errors.name && (
              <p className="text-xs text-red-600 mt-1" id="agent-name-error">
                {errors.name}
              </p>
            )}
          </div>

          <div>
            <label htmlFor="agent-model" className="block text-sm font-medium">
              Model
            </label>
            <input
              className="mt-1 w-full rounded border px-3 py-2 text-sm"
              id="agent-model"
              value={form.model}
              onChange={(e) => setField('model', e.target.value)}
              maxLength={80}
              placeholder="gpt-4o-mini"
              title="Model name (up to 80 characters)"
              {...(errors.model ? { 'aria-invalid': 'true' } : {})}
              aria-describedby={`agent-model-help${errors.model ? ' agent-model-error' : ''}`}
            />
            <p id="agent-model-help" className="text-xs text-gray-600 mt-1">
              Up to 80 characters.
            </p>
            {errors.model && (
              <p className="text-xs text-red-600 mt-1" id="agent-model-error">
                {errors.model}
              </p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-4 items-center">
            <div>
              <label htmlFor="agent-temperature" className="block text-sm font-medium">
                Temperature: {form.temperature.toFixed(1)}
              </label>
              <input
                type="range"
                min={0}
                max={2}
                step={0.1}
                value={form.temperature}
                onChange={(e) => setField('temperature', Number(e.target.value))}
                id="agent-temperature"
                className="w-full"
                title="Temperature (0 to 2)"
                {...(errors.temperature ? { 'aria-invalid': 'true' } : {})}
                aria-describedby={`agent-temperature-help${errors.temperature ? ' agent-temperature-error' : ''}`}
              />
              <p id="agent-temperature-help" className="text-xs text-gray-600 mt-1">
                Range 0–2. Lower = precise, higher = creative.
              </p>
              {errors.temperature && (
                <p className="text-xs text-red-600 mt-1" id="agent-temperature-error">
                  {errors.temperature}
                </p>
              )}
            </div>
            <div>
              <label htmlFor="agent-maxsteps" className="block text-sm font-medium">
                Max steps
              </label>
              <input
                type="number"
                min={1}
                max={50}
                value={form.maxSteps}
                onChange={(e) => setField('maxSteps', Number(e.target.value))}
                id="agent-maxsteps"
                className="mt-1 w-full rounded border px-3 py-2 text-sm"
                title="Max steps (1 to 50)"
                {...(errors.maxSteps ? { 'aria-invalid': 'true' } : {})}
                aria-describedby={`agent-maxsteps-help${errors.maxSteps ? ' agent-maxsteps-error' : ''}`}
              />
              <p id="agent-maxsteps-help" className="text-xs text-gray-600 mt-1">
                Range 1–50.
              </p>
              {errors.maxSteps && (
                <p className="text-xs text-red-600 mt-1" id="agent-maxsteps-error">
                  {errors.maxSteps}
                </p>
              )}
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium">Enabled</label>
            <label className="inline-flex items-center gap-2 mt-1 text-sm">
              <input
                type="checkbox"
                checked={form.isEnabled !== false}
                onChange={(e) => setField('isEnabled', e.target.checked)}
                aria-label="Enabled"
              />
              <span>{form.isEnabled !== false ? 'Enabled' : 'Disabled'}</span>
            </label>
            <p className="text-xs text-gray-600 mt-1">
              When disabled, this agent cannot be run or used by tasks.
            </p>
          </div>

          <div>
            <label htmlFor="agent-system-prompt" className="block text-sm font-medium">
              System Prompt
            </label>
            <textarea
              className="mt-1 w-full rounded border px-3 py-2 text-sm font-mono"
              rows={8}
              value={form.systemPrompt}
              onChange={(e) => setField('systemPrompt', e.target.value)}
              placeholder={'You are a helpful agent...'}
              id="agent-system-prompt"
              title="System prompt"
              aria-describedby="agent-system-prompt-help"
            />
            <p id="agent-system-prompt-help" className="text-xs text-gray-600 mt-1">
              Describe the agent’s behavior and constraints. The token estimate updates as you type.
            </p>
          </div>
        </div>

        <div className="space-y-4">
          <div className="flex items-center gap-2">
            <h3 className="font-medium">Live Preview</h3>
            <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-xs text-gray-800">
              ~{tokenEstimate} tokens
            </span>
            {toolWarning && (
              <span className="inline-flex items-center rounded bg-yellow-100 px-2 py-0.5 text-xs text-yellow-800">
                Prompt suggests using tools, but none selected
              </span>
            )}
          </div>
          <pre className="rounded bg-gray-50 p-3 text-xs max-h-64 overflow-auto">
            <code>{form.systemPrompt || 'Preview will appear here as you type...'}</code>
          </pre>

          <div>
            <h3 className="font-medium mb-2">Tools</h3>
            <p className="text-xs text-gray-600 mb-2">
              Select which tools the agent is allowed to use.
            </p>
            {toolWarning && (
              <div
                role="alert"
                className="text-xs text-yellow-800 bg-yellow-50 border border-yellow-200 rounded px-2 py-1 mb-2"
              >
                Your prompt suggests tool usage — consider allowing relevant tools.
              </div>
            )}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              {tools.map((t) => (
                <label key={t.name} className="flex items-start gap-2 rounded border p-2 text-sm">
                  <input
                    type="checkbox"
                    className="mt-1"
                    checked={form.toolAllowlist.includes(t.name)}
                    onChange={() => onToggleTool(t.name)}
                  />
                  <span>
                    <span className="font-medium">{t.name}</span>{' '}
                    <span className="text-xs inline-block rounded bg-gray-100 px-1 text-gray-700">
                      {t.category}
                    </span>
                    <div className="text-gray-600 text-xs mt-1">{t.description}</div>
                  </span>
                </label>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="flex items-center justify-end gap-2">
        <button
          type="button"
          className="px-3 py-2 rounded border text-sm"
          onClick={() => router.push('/studio/agents')}
          disabled={saving}
        >
          Cancel
        </button>
        <button
          type="button"
          className="px-3 py-2 rounded bg-blue-600 text-white text-sm disabled:opacity-60"
          onClick={onSubmit}
          disabled={saving}
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
      </div>
    </div>
  );
}
