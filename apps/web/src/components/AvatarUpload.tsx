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
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import { ConfirmDialog } from './ui/ConfirmDialog';

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
  const [statusMsg, setStatusMsg] = React.useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = React.useState(false);
  // preview — URL shown in Avatar. Starts as object URL for local file, replaced with server URL on upload success.
  const [preview, setPreview] = React.useState<string | null>(null);
  const previousObjectUrlRef = React.useRef<string | null>(null);
  const inputRef = React.useRef<HTMLInputElement | null>(null);

  function chooseFile() {
    inputRef.current?.click();
  }

  function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    setError(null);
    setStatusMsg(null);
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
    // Revoke any prior blob URL to avoid memory leak.
    if (previousObjectUrlRef.current) {
      try {
        if (typeof URL.revokeObjectURL === 'function') {
          URL.revokeObjectURL(previousObjectUrlRef.current);
        }
      } catch {
        // Ignore revoke failures in test envs
      }
    }
    const objectUrl = URL.createObjectURL(f);
    previousObjectUrlRef.current = objectUrl;
    setPreview(objectUrl);
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
        setPreview(cacheBusted);
        if (previousObjectUrlRef.current) {
          try {
            if (typeof URL.revokeObjectURL === 'function') {
              URL.revokeObjectURL(previousObjectUrlRef.current);
            }
          } catch {
            // Ignore revoke failures
          }
          previousObjectUrlRef.current = null;
        }
        if (typeof window !== 'undefined') {
          window.dispatchEvent(new CustomEvent('avatar-updated', { detail: { url: cacheBusted } }));
        }
        onUploaded?.(cacheBusted);
        setFile(null);
        setStatusMsg('Avatar updated.');
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setSubmitting(false);
    }
  }

  function requestClear() {
    setError(null);
    setStatusMsg(null);
    const isBlob = typeof preview === 'string' && preview.startsWith('blob:');
    if (!file && !isBlob) return;
    setConfirmOpen(true);
  }

  function handleClearConfirmed() {
    if (previousObjectUrlRef.current) {
      try {
        if (typeof URL.revokeObjectURL === 'function') {
          URL.revokeObjectURL(previousObjectUrlRef.current);
        }
      } catch {
        // ignore
      }
      previousObjectUrlRef.current = null;
    }
    setFile(null);
    setPreview(null);
    setStatusMsg('Selection cleared.');
  }

  React.useEffect(() => {
    return () => {
      if (previousObjectUrlRef.current) {
        try {
          if (typeof URL.revokeObjectURL === 'function') {
            URL.revokeObjectURL(previousObjectUrlRef.current);
          }
        } catch {
          // ignore
        }
        previousObjectUrlRef.current = null;
      }
    };
  }, []);

  return (
    <>
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
            sx={{
              width: 56,
              height: 56,
              border: '1px solid',
              borderColor: 'divider',
              overflow: 'hidden',
              '& img': {
                width: '100%',
                height: '100%',
                objectFit: 'cover',
                display: 'block',
              },
            }}
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
          <Tooltip title="Clear selection">
            <span>
              <Button
                size="small"
                color="error"
                variant="outlined"
                startIcon={<DeleteOutlineIcon />}
                onClick={requestClear}
                disabled={submitting || (!file && !(preview && preview.startsWith('blob:')))}
                aria-label="Clear selection"
              >
                Clear
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
          {statusMsg && !error && (
            <Typography role="status" variant="caption" color="success.main" display="block">
              {statusMsg}
            </Typography>
          )}
          {error && (
            <Typography role="alert" variant="caption" color="error.main" display="block">
              {error}
            </Typography>
          )}
        </Box>
      </Stack>
      <ConfirmDialog
        open={confirmOpen}
        title="Clear selection?"
        description="This will discard the image you just selected."
        confirmLabel="Clear"
        cancelLabel="Cancel"
        onConfirm={handleClearConfirmed}
        onClose={() => setConfirmOpen(false)}
      />
    </>
  );
}
