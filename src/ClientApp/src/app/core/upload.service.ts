import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  CompletePart,
  InitUploadResponse,
  PartUrlResponse,
  UploadTask,
} from './models';

/**
 * Presigned S3 multipart upload, browser -> R2 directly. See docs/ai-design-decisions.md (D2).
 *
 * Flow per file:
 *   1. POST /api/uploads/init            -> { mediaId, uploadId, partSize }
 *   2. per part: GET .../part-url        -> presigned PUT URL
 *      then fetch(PUT) the slice to storage, read the ETag response header
 *   3. POST .../complete { parts }       -> reconciles quota, marks ready
 *   on failure: POST .../abort           -> releases reserved quota
 *
 * NOTE: the ETag response header is only readable if the bucket's CORS config lists
 * `ETag` under Access-Control-Expose-Headers. Configure this on R2 / MinIO.
 */
@Injectable({ providedIn: 'root' })
export class UploadService {
  private readonly http = inject(HttpClient);

  /** Live list of upload tasks for the UI to render. */
  readonly tasks = signal<UploadTask[]>([]);

  async upload(file: File): Promise<void> {
    const task: UploadTask = {
      id: crypto.randomUUID(),
      fileName: file.name,
      totalBytes: file.size,
      uploadedBytes: 0,
      status: 'uploading',
    };
    this.tasks.update((t) => [task, ...t]);

    let init: InitUploadResponse | undefined;
    try {
      init = await firstValueFrom(
        this.http.post<InitUploadResponse>('/api/uploads/init', {
          originalName: file.name,
          sizeBytes: file.size,
          contentType: file.type || 'application/octet-stream',
        })
      );

      const parts = await this.uploadParts(file, init, task);

      this.patch(task.id, { status: 'completing' });
      await firstValueFrom(
        this.http.post(`/api/uploads/${init.mediaId}/complete`, { parts })
      );

      this.patch(task.id, { status: 'done', uploadedBytes: file.size });
    } catch (err: unknown) {
      if (init) {
        // Best-effort: release the reserved quota server-side.
        await firstValueFrom(
          this.http.post(`/api/uploads/${init.mediaId}/abort`, {})
        ).catch(() => void 0);
      }
      this.patch(task.id, {
        status: 'error',
        error: err instanceof Error ? err.message : 'Upload failed',
      });
      throw err;
    }
  }

  private async uploadParts(
    file: File,
    init: InitUploadResponse,
    task: UploadTask
  ): Promise<CompletePart[]> {
    const parts: CompletePart[] = [];
    const partCount = Math.max(1, Math.ceil(file.size / init.partSize));

    for (let partNumber = 1; partNumber <= partCount; partNumber++) {
      const start = (partNumber - 1) * init.partSize;
      const end = Math.min(start + init.partSize, file.size);
      const blob = file.slice(start, end);

      const { url } = await firstValueFrom(
        this.http.get<PartUrlResponse>(
          `/api/uploads/${init.mediaId}/part-url`,
          { params: { partNumber } }
        )
      );

      // Direct-to-storage PUT via fetch so the auth interceptor doesn't touch it.
      const res = await fetch(url, { method: 'PUT', body: blob });
      if (!res.ok) {
        throw new Error(`Part ${partNumber} failed: HTTP ${res.status}`);
      }
      const eTag = res.headers.get('ETag');
      if (!eTag) {
        throw new Error(
          `Part ${partNumber}: ETag not readable. Add ETag to the bucket CORS expose-headers.`
        );
      }

      parts.push({ partNumber, eTag });
      this.patch(task.id, { uploadedBytes: end });
    }

    return parts;
  }

  private patch(id: string, changes: Partial<UploadTask>): void {
    this.tasks.update((tasks) =>
      tasks.map((t) => (t.id === id ? { ...t, ...changes } : t))
    );
  }
}
