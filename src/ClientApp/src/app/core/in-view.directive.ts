import {
  Directive,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  inject,
} from '@angular/core';

/**
 * Emits once, the first time the host element is near the viewport.
 *
 * Used to defer thumbnail work: a folder can hold hundreds of cards, and presigning a URL for
 * every one on load would mean hundreds of round trips for images nobody scrolls to. The
 * observer disconnects after firing, so each card costs at most one request.
 */
@Directive({ selector: '[appInView]' })
export class InViewDirective implements OnInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private observer?: IntersectionObserver;

  @Output('appInView') readonly inView = new EventEmitter<void>();

  ngOnInit(): void {
    // Start a little before the card is actually visible so thumbnails are usually ready by the
    // time it scrolls in.
    this.observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((e) => e.isIntersecting)) return;
        this.disconnect();
        this.inView.emit();
      },
      { rootMargin: '200px' }
    );
    this.observer.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.disconnect();
  }

  private disconnect(): void {
    this.observer?.disconnect();
    this.observer = undefined;
  }
}
