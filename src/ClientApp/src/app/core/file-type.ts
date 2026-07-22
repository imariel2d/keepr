import { FileType, TYPE_META } from '../cove/lib/files/file-type-meta';

/** Map a MIME type to the Cove file-type used for icons and accent colours. */
export function fileTypeOf(contentType: string | null | undefined): FileType {
  const t = contentType ?? '';
  if (t.startsWith('image/')) return 'image';
  if (t.startsWith('video/')) return 'video';
  if (t.startsWith('audio/')) return 'audio';
  if (t === 'application/pdf') return 'pdf';
  if (t.includes('zip') || t.includes('compressed') || t.includes('tar')) return 'archive';
  if (t.includes('word') || t.includes('document')) return 'doc';
  if (t.includes('sheet') || t.includes('excel') || t.includes('csv')) return 'sheet';
  return 'default';
}

export function typeMetaOf(contentType: string | null | undefined): { icon: string; color: string } {
  return TYPE_META[fileTypeOf(contentType)];
}

/** e.g. "Jul 12, 2026". */
export function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}
