import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { FolderContents, FolderItem } from './models';

/**
 * Folder tree operations. See docs/api-changes-frontend.md §2.
 *
 * Every write can return a name different from the one requested — the server auto-suffixes
 * collisions instead of failing — so callers must use the name from the response.
 */
@Injectable({ providedIn: 'root' })
export class FolderService {
  private readonly http = inject(HttpClient);

  /** Subfolders, files, and breadcrumbs for one folder. Pass null for the root. */
  contents(folderId: string | null): Promise<FolderContents> {
    let params = new HttpParams();
    if (folderId) params = params.set('folderId', folderId);
    return firstValueFrom(
      this.http.get<FolderContents>('/api/folders/contents', { params })
    );
  }

  create(name: string, parentId: string | null): Promise<FolderItem> {
    return firstValueFrom(
      this.http.post<FolderItem>('/api/folders', { name, parentId })
    );
  }

  rename(id: string, name: string): Promise<FolderItem> {
    return firstValueFrom(this.http.patch<FolderItem>(`/api/folders/${id}`, { name }));
  }

  /** `parentId: null` moves to the root. 409 if the destination is inside the moved folder. */
  move(id: string, parentId: string | null): Promise<FolderItem> {
    return firstValueFrom(
      this.http.post<FolderItem>(`/api/folders/${id}/move`, { parentId })
    );
  }

  /** Moves the folder and everything under it to the trash. Reversible for 10 days. */
  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/folders/${id}`));
  }
}
