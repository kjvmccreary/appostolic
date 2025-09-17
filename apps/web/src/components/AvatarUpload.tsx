'use client';
import React from 'react';
import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';

/**
 * AvatarUpload
 * Styled avatar uploader using MUI components. Responsibilities:
 *  - Provide accessible file selection (hidden input + trigger button)
 *  - Validate type & size (<=2MB, jpeg/png/webp)
 *  - Preview selected image immediately
 *  - Upload to /api-proxy/users/me/avatar and dispatch global 'avatar-updated' event
 *  - Expose optional onUploaded callback for downstream usage
 */
type Props = { onUploaded?: (url: string) => void };

export function AvatarUpload({ onUploaded }: Props) {
  const [file, setFile] = React.useState<File | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [submitting, setSubmitting] = React.useState(false);
  const [preview, setPreview] = React.useState<string | null>(null);
  const inputRef = React.useRef<HTMLInputElement | null>(null);

  function chooseFile() {
    inputRef.current?.click();
  }

  function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    setError(null);
    const f = e.target.files?.[0] ?? null;
    if (!f) {
      setFile(null);
      setPreview(null);
      return;
    }
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(f.type)) {
      setError('Only PNG, JPEG, or WebP images are allowed.');
      return;
    }
    if (f.size > 2 * 1024 * 1024) {
      setError('File is too large (max 2MB).');
      return;
    }
    setFile(f);
    setPreview(URL.createObjectURL(f));
  }

  async function handleUpload() {
    setError(null);
    if (!file) return;
    setSubmitting(true);
    try {
      const fd = new FormData();
      fd.set('file', file);
      const res = await fetch('/api-proxy/users/me/avatar', { method: 'POST', body: fd });
      if (!res.ok) {
        const text = await res.text();
        setError(`Upload failed (${res.status}): ${text || 'Unknown error'}`);
        return;
      }
      const data = (await res.json()) as { avatar?: { url?: string } };
      const url = data?.avatar?.url;
      if (url) {
        const cacheBusted = `${url}${url.includes('?') ? '&' : '?'}v=${Date.now()}`;
        if (typeof window !== 'undefined') {
          window.dispatchEvent(new CustomEvent('avatar-updated', { detail: { url: cacheBusted } }));
        }
        onUploaded?.(cacheBusted);
        // Reset local selection but keep preview (shows uploaded)
        setFile(null);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Stack
      direction="row"
      spacing={2}
      alignItems="center"
      component="section"
      aria-label="Avatar upload"
    >
      <Box position="relative">
        <Avatar
          src={preview || undefined}
          alt="Avatar preview"
          sx={{ width: 56, height: 56, border: '1px solid', borderColor: 'divider' }}
        />
        {submitting && (
          <LinearProgress
            color="primary"
            sx={{ position: 'absolute', bottom: -4, left: 0, width: '100%', height: 4 }}
          />
        )}
      </Box>
      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/jpeg,image/webp"
        onChange={onChange}
        aria-label="Choose avatar image"
        hidden
        disabled={submitting}
      />
      <Stack direction="row" spacing={1} alignItems="center">
        <Tooltip title="Select image">
          <span>
            <Button
              size="small"
              variant="outlined"
              startIcon={<PhotoCameraIcon />}
              onClick={chooseFile}
              disabled={submitting}
            >
              Choose
            </Button>
          </span>
        </Tooltip>
        <Tooltip title="Upload selected image">
          <span>
            <Button
              size="small"
              variant="contained"
              startIcon={<CloudUploadIcon />}
              onClick={handleUpload}
              disabled={!file || submitting}
            >
              {submitting ? 'Uploading…' : 'Upload'}
            </Button>
          </span>
        </Tooltip>
      </Stack>
      <Box minWidth={160}>
        {file && !error && (
          <Typography variant="caption" color="text.secondary" display="block" noWrap>
            {file.name} ({(file.size / 1024).toFixed(0)} KB)
          </Typography>
        )}
        {error && (
          <Typography role="alert" variant="caption" color="error.main" display="block">
            {error}
          </Typography>
        )}
      </Box>
    </Stack>
  );
}
