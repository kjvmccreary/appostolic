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

type Props = { initialUrl?: string | null; onUploaded?: (url: string) => void };

export function TenantLogoUpload({ initialUrl, onUploaded }: Props) {
  const [file, setFile] = React.useState<File | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [submitting, setSubmitting] = React.useState(false);
  const [deleting, setDeleting] = React.useState(false);
  const [statusMsg, setStatusMsg] = React.useState<string | null>(null);
  const [preview, setPreview] = React.useState<string | null>(initialUrl || null);
  const prevObjUrlRef = React.useRef<string | null>(null);
  const inputRef = React.useRef<HTMLInputElement | null>(null);

  function chooseFile() {
    inputRef.current?.click();
  }

  function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    setError(null);
    const f = e.target.files?.[0] ?? null;
    if (!f) {
      setFile(null);
      setPreview(initialUrl || null);
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
    if (prevObjUrlRef.current) {
      try {
        if (typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(prevObjUrlRef.current);
      } catch {
        // noop
      }
    }
    const objectUrl = URL.createObjectURL(f);
    prevObjUrlRef.current = objectUrl;
    setPreview(objectUrl);
  }

  async function handleUpload() {
    if (!file) return;
    setSubmitting(true);
    setError(null);
    setStatusMsg(null);
    try {
      const fd = new FormData();
      fd.set('file', file);
      const res = await fetch('/api-proxy/tenants/logo', { method: 'POST', body: fd });
      if (!res.ok) {
        const text = await res.text();
        setError(`Upload failed (${res.status}): ${text || 'Unknown error'}`);
        return;
      }
      const data = (await res.json()) as { logo?: { url?: string } };
      const url = data?.logo?.url;
      if (url) {
        const cacheBusted = `${url}${url.includes('?') ? '&' : '?'}v=${Date.now()}`;
        setPreview(cacheBusted);
        if (prevObjUrlRef.current) {
          try {
            if (typeof URL.revokeObjectURL === 'function')
              URL.revokeObjectURL(prevObjUrlRef.current);
          } catch {
            // noop
          }
          prevObjUrlRef.current = null;
        }
        onUploaded?.(cacheBusted);
        setFile(null);
        setStatusMsg('Logo updated.');
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemove() {
    setError(null);
    setStatusMsg(null);
    // If a local file is selected (blob preview), clear selection without network
    const isBlob = typeof preview === 'string' && preview.startsWith('blob:');
    if (file || isBlob) {
      if (prevObjUrlRef.current) {
        try {
          if (typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(prevObjUrlRef.current);
        } catch {
          // noop
        }
        prevObjUrlRef.current = null;
      }
      setFile(null);
      setPreview(initialUrl || null);
      setStatusMsg('Selection cleared.');
      return;
    }
    // Otherwise attempt server delete
    if (!preview) return; // nothing to remove
    setDeleting(true);
    try {
      const res = await fetch('/api-proxy/tenants/logo', { method: 'DELETE' });
      if (!res.ok) {
        const text = await res.text();
        setError(`Delete failed (${res.status}): ${text || 'Unknown error'}`);
        return;
      }
      setPreview(null);
      setStatusMsg('Logo removed.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setDeleting(false);
    }
  }

  React.useEffect(() => {
    return () => {
      if (prevObjUrlRef.current) {
        try {
          if (typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(prevObjUrlRef.current);
        } catch {
          // noop
        }
        prevObjUrlRef.current = null;
      }
    };
  }, []);

  return (
    <Stack direction="row" spacing={2} alignItems="center" aria-label="Tenant logo upload">
      <Box position="relative">
        <Avatar
          src={preview || undefined}
          alt="Logo preview"
          sx={{
            width: 56,
            height: 56,
            border: '1px solid',
            borderColor: 'divider',
            overflow: 'hidden',
            '& img': { width: '100%', height: '100%', objectFit: 'contain', display: 'block' },
          }}
        />
        {(submitting || deleting) && (
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
        aria-label="Choose tenant logo image"
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
              disabled={submitting || deleting}
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
              disabled={!file || submitting || deleting}
            >
              {submitting ? 'Uploading…' : 'Upload'}
            </Button>
          </span>
        </Tooltip>
        <Tooltip title="Remove logo">
          <span>
            <Button
              size="small"
              color="error"
              variant="outlined"
              startIcon={<DeleteOutlineIcon />}
              onClick={handleRemove}
              disabled={submitting || deleting || (!preview && !file)}
              aria-label="Remove logo"
            >
              {deleting ? 'Removing…' : 'Remove'}
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
  );
}
