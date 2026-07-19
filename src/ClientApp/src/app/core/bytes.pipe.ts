import { Pipe, PipeTransform } from '@angular/core';

/** Formats a byte count as a human-readable size, e.g. 1536 -> "1.5 KB". */
@Pipe({ name: 'bytes' })
export class BytesPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    if (value == null || value < 0) return '—';
    if (value === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(value) / Math.log(1024));
    const size = value / Math.pow(1024, i);
    return `${size.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
  }
}
