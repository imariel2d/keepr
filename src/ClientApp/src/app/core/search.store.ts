import { Injectable, signal } from '@angular/core';

/**
 * The live text in the topbar search box, shared between the app shell (which owns the input and
 * debounces navigation) and the Files view (which renders results).
 *
 * It exists so the Files view can show a skeleton *during the debounce window* — before the term
 * reaches the URL and a fetch even starts. The view compares this live term against the query
 * that's actually loaded (`?q=`): while they differ, a new search is pending.
 */
@Injectable({ providedIn: 'root' })
export class SearchStore {
  readonly term = signal('');
}
