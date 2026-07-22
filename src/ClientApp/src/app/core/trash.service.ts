import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { RestoreResponse, TrashItem } from './models';

/**
 * The trash. Deleting a file or folder puts it here; it stays recoverable until `purgesAt`,
 * then a server-side sweeper removes it permanently. See docs/trash-soft-delete-design.md.
 *
 * Trashed bytes keep counting against the quota until purged — that's what `usage.trashedBytes`
 * is for.
 */
@Injectable({ providedIn: 'root' })
export class TrashService {
  private readonly http = inject(HttpClient);

  /** Only items deleted directly: a trashed folder appears once, not once per file inside. */
  list(): Promise<TrashItem[]> {
    return firstValueFrom(this.http.get<TrashItem[]>('/api/trash'));
  }

  /**
   * Restores an item and everything trashed alongside it. The returned name may differ from
   * the deleted one, and an item whose folder was purged comes back at the root.
   */
  restore(id: string): Promise<RestoreResponse> {
    return firstValueFrom(
      this.http.post<RestoreResponse>(`/api/trash/${id}/restore`, {})
    );
  }

  /** Permanent. Frees quota immediately. */
  purge(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/trash/${id}`));
  }

  /** Permanent, for everything in the trash. */
  empty(): Promise<void> {
    return firstValueFrom(this.http.delete<void>('/api/trash'));
  }
}
