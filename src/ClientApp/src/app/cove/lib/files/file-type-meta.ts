export type FileType = 'image' | 'pdf' | 'doc' | 'sheet' | 'video' | 'audio' | 'archive' | 'folder' | 'default';
export const TYPE_META: Record<FileType, { icon: string; color: string }> = {
  image: { icon: 'image', color: 'var(--teal-500)' },
  pdf: { icon: 'file-text', color: 'var(--danger)' },
  doc: { icon: 'file-text', color: '#3B6FD6' },
  sheet: { icon: 'grid-3x3', color: 'var(--teal-600)' },
  video: { icon: 'film', color: 'var(--brand-600)' },
  audio: { icon: 'music', color: 'var(--amber-600)' },
  archive: { icon: 'file-archive', color: 'var(--text-secondary)' },
  folder: { icon: 'folder', color: 'var(--teal-500)' },
  default: { icon: 'file', color: 'var(--text-tertiary)' },
};
