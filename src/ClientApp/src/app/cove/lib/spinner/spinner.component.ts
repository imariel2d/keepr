import { Component, Input } from '@angular/core';

/**
 * Indeterminate loading spinner. A rotating ring, sized and coloured via inputs.
 *
 * The global `prefers-reduced-motion` guard (styles.scss) stops the rotation for users who ask
 * for less motion — they get a static ring, which is why callers should pair it with text.
 */
@Component({
  selector: 'cove-spinner',
  standalone: true,
  template: `<span class="spinner" role="status" [attr.aria-label]="label"
    [style.width.px]="size" [style.height.px]="size"
    [style.borderColor]="track" [style.borderTopColor]="color"></span>`,
  styles: [`
    .spinner {
      display: inline-block;
      box-sizing: border-box;
      border: 2px solid;
      border-radius: 50%;
      animation: cove-spin 0.7s linear infinite;
    }
    @keyframes cove-spin { to { transform: rotate(360deg); } }
  `],
})
export class SpinnerComponent {
  @Input() size = 20;
  /** The moving arc. */
  @Input() color = 'var(--accent)';
  /** The static remainder of the ring. */
  @Input() track = 'var(--border-default)';
  @Input() label = 'Loading';
}
