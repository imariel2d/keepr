import { Injectable, inject, signal } from '@angular/core';
import { MediaService } from './media.service';
import { Usage } from './models';

/**
 * Shared quota state for the sidebar meter.
 *
 * It lives outside the routed views because the meter is in the app shell but changes as a
 * result of things the views do — uploading, trashing, restoring, purging. Views call
 * {@link refresh} after any of those; the shell just reads the signal.
 */
@Injectable({ providedIn: 'root' })
export class UsageStore {
  private readonly media = inject(MediaService);

  readonly usage = signal<Usage | null>(null);

  async refresh(): Promise<void> {
    try {
      this.usage.set(await this.media.usage());
    } catch {
      // A failed meter refresh must never break the action that triggered it.
    }
  }
}
