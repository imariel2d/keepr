import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SearchResults } from './models';

/**
 * Owner-scoped name search across the whole folder tree. Matches files and folders by a
 * case-insensitive substring; each hit carries its `location` (containing-folder path) because
 * results span folders. See docs/search-design.md.
 */
@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);

  /**
   * Files and folders whose name contains `q`. The caller is expected to skip empty terms — the
   * server rejects them with a 400, since an empty box means "browse", not "search".
   */
  search(q: string): Promise<SearchResults> {
    const params = new HttpParams().set('q', q);
    return firstValueFrom(this.http.get<SearchResults>('/api/search', { params }));
  }
}
