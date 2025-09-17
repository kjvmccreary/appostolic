'use client';
import React, { useState, useMemo } from 'react';
import Box from '@mui/material/Box';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Tooltip from '@mui/material/Tooltip';
import IconButton from '@mui/material/IconButton';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import PreviewIcon from '@mui/icons-material/Preview';
import EditIcon from '@mui/icons-material/Edit';
import ClearIcon from '@mui/icons-material/Clear';
import SaveIcon from '@mui/icons-material/Save';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
// remark-breaks converts single newlines to line breaks like GitHub soft wrap behavior.
// Types provided via local ambient declaration if package lacks them.
import remarkBreaks from 'remark-breaks';

/**
 * BioEditor (UPROF-10)
 * Purpose: Allow a user to edit their profile bio (markdown source) and submit a minimal
 * JSON merge patch to the profile endpoint via the web proxy. The API is expected to
 * sanitize and (optionally) render markdown → sanitized HTML on read; here we only send
 * the raw markdown content and a format indicator.
 *
 * Semantics:
 * - If the user clears the bio (empty string) and clicks Save, we send `{ "bio": null }`
 *   so the server removes the field (null = clear) respecting existing deep merge rules.
 * - If unchanged, the Save button stays disabled.
 * - Basic client-side length guard (e.g., 4000 chars) to prevent overly large payloads; server remains authority.
 * - Accessible: textarea labeled, character count announced via `aria-describedby`, status region for success/error.
 */

interface BioContent { format?: string; content?: string }

interface BioEditorProps {
  initial?: BioContent | null;
  maxChars?: number;
  onSaved?: (next: { format: string; content: string } | null) => void;
}

export const BioEditor: React.FC<BioEditorProps> = ({ initial, maxChars = 4000, onSaved }) => {
  // baseline tracks last-saved state so we can compute a diff and reset dirty after save
  const [baseline, setBaseline] = useState<BioContent | null>(initial ?? null);
  const [value, setValue] = useState(initial?.content ?? '');
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState<'idle' | 'success' | 'error'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<0 | 1>(0); // 0 = write, 1 = preview
  const dirty = value !== (baseline?.content ?? '');
  const overLimit = value.length > maxChars;
  const previewContent = useMemo(() => value, [value]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!dirty || overLimit) return;
    setSaving(true);
    setStatus('idle');
    setError(null);
    try {
      // Build minimal patch: only include bio key if changed vs baseline
  const body: Record<string, unknown> = {};
      const trimmed = value.trim();
      const baselineContent = baseline?.content ?? '';
      if (trimmed === '' && baselineContent !== '') {
        body.bio = null;
      } else if (trimmed !== '' && trimmed !== baselineContent) {
        body.bio = { format: 'markdown', content: value };
      }
      if (Object.keys(body).length === 0) {
        setSaving(false);
        return; // nothing to send
      }
      const res = await fetch('/api-proxy/users/me', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        setStatus('success');
        if (body.bio === null) {
          setBaseline(null);
          onSaved?.(null);
        } else if (body.bio) {
          // update baseline to new value
            setBaseline({ format: 'markdown', content: value });
            onSaved?.({ format: 'markdown', content: value });
        }
      } else {
        setStatus('error');
        setError(`Failed to save bio (status ${res.status})`);
      }
    } catch (err) {
      setStatus('error');
      const msg = err instanceof Error ? err.message : 'Unexpected error saving bio';
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  function handleClear() {
    if (!value) return;
    setValue('');
  }

  return (
    <Box
      component="form"
      onSubmit={handleSubmit}
      aria-labelledby="bio-heading"
      sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}
    >
      <Box>
        <Typography id="bio-heading" variant="h6" component="h2" sx={{ mb: 0.5 }}>
          Bio
        </Typography>
        <Typography variant="body2" color="text.secondary" id="bio-help">
          Write a short introduction. Supports GitHub-flavored Markdown. {maxChars} characters max.
        </Typography>
      </Box>
      <Paper variant="outlined" sx={{ display: 'flex', flexDirection: 'column' }}>
        <Tabs
          value={tab}
          onChange={(_, v) => setTab(v)}
          aria-label="Bio editor tabs"
          textColor="primary"
          indicatorColor="primary"
          variant="fullWidth"
          sx={{ minHeight: 38 }}
        >
          <Tab
            icon={<EditIcon fontSize="small" />}
            iconPosition="start"
            label="Write"
            id="bio-tab-write"
            aria-controls="bio-tabpanel-write"
            sx={{ minHeight: 38 }}
          />
          <Tab
            icon={<PreviewIcon fontSize="small" />}
            iconPosition="start"
            label="Preview"
            id="bio-tab-preview"
            aria-controls="bio-tabpanel-preview"
            sx={{ minHeight: 38 }}
          />
        </Tabs>
        <Divider />
        {tab === 0 && (
          <Box
            role="tabpanel"
            id="bio-tabpanel-write"
            aria-labelledby="bio-tab-write"
            sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 1 }}
          >
            <TextField
              multiline
              minRows={6}
              maxRows={18}
              id="bio"
              name="bio"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              aria-describedby="bio-help bio-count"
              error={overLimit}
              helperText={overLimit ? 'Bio is too long.' : ' '}
              disabled={saving}
              placeholder="Write your bio in Markdown..."
            />
            <Stack direction="row" spacing={1} alignItems="center" justifyContent="space-between">
              <Typography
                id="bio-count"
                variant="caption"
                color={overLimit ? 'error.main' : 'text.secondary'}
              >
                {value.length}/{maxChars}
              </Typography>
              <Stack direction="row" spacing={1}>
                <Tooltip title="Copy raw markdown">
                  <span>
                    <IconButton
                      size="small"
                      aria-label="Copy bio"
                      onClick={() => {
                        navigator.clipboard.writeText(value);
                      }}
                      disabled={!value}
                    >
                      <ContentCopyIcon fontSize="inherit" />
                    </IconButton>
                  </span>
                </Tooltip>
                <Tooltip title="Clear">
                  <span>
                    <IconButton
                      size="small"
                      aria-label="Clear bio"
                      onClick={handleClear}
                      disabled={!value || saving}
                    >
                      <ClearIcon fontSize="inherit" />
                    </IconButton>
                  </span>
                </Tooltip>
              </Stack>
            </Stack>
          </Box>
        )}
        {tab === 1 && (
          <Box
            role="tabpanel"
            id="bio-tabpanel-preview"
            aria-labelledby="bio-tab-preview"
            sx={{ p: 2 }}
          >
            {previewContent.trim() ? (
              <Box
                sx={{
                  typography: 'body2',
                  '& h1, & h2, & h3': { mt: 2 },
                  '& pre': { p: 1, bgcolor: 'grey.100', overflowX: 'auto', borderRadius: 1 },
                  '& code': {
                    fontFamily: 'monospace',
                    bgcolor: 'grey.100',
                    px: 0.5,
                    borderRadius: 0.5,
                  },
                }}
              >
                <Markdown remarkPlugins={[remarkGfm, remarkBreaks]}>{previewContent}</Markdown>
              </Box>
            ) : (
              <Typography variant="body2" color="text.disabled">
                Nothing to preview yet.
              </Typography>
            )}
          </Box>
        )}
      </Paper>
      <Stack direction="row" spacing={2} alignItems="center">
        <Button
          type="submit"
          variant="contained"
          size="small"
          startIcon={<SaveIcon />}
          disabled={!dirty || overLimit || saving}
        >
          {saving ? 'Saving…' : 'Save Bio'}
        </Button>
        {status === 'success' && (
          <Typography role="status" variant="body2" color="success.main">
            Bio saved
          </Typography>
        )}
        {status === 'error' && (
          <Typography role="alert" variant="body2" color="error.main">
            {error || 'Failed to save bio'}
          </Typography>
        )}
      </Stack>
    </Box>
  );
};

export default BioEditor;
