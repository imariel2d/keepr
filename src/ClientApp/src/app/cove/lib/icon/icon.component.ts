import { Component, Input, ElementRef, AfterViewInit, OnChanges } from '@angular/core';

declare const lucide: any;

/** Renders a Lucide icon by name. Requires lucide loaded globally (CDN or npm). */
@Component({
  selector: 'cove-icon',
  standalone: true,
  template: `<i [attr.data-lucide]="name"
               [style.width.px]="size" [style.height.px]="size"
               [style.display]="'inline-flex'" [style.flexShrink]="0"
               [style.color]="color || 'currentColor'"></i>`,
})
export class IconComponent implements AfterViewInit, OnChanges {
  @Input() name!: string;
  @Input() size = 18;
  @Input() color?: string;
  constructor(private el: ElementRef) {}
  private render() { if (typeof lucide !== 'undefined') lucide.createIcons({ nameAttr: 'data-lucide', root: this.el.nativeElement }); }
  ngAfterViewInit() { this.render(); }
  ngOnChanges() { queueMicrotask(() => this.render()); }
}
