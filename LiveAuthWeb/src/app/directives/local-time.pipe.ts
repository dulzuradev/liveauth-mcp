import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'localTime',
  standalone: true
})
export class LocalTimePipe implements PipeTransform {
  transform(
    value: number | string | Date | null | undefined,
    format: string = 'short'
  ): string {
    if (!value) return '—';

    let date: Date;

    if (typeof value === 'number') {
      // Heuristic: seconds vs milliseconds
      date = value < 1e12
        ? new Date(value * 1000) // seconds → ms
        : new Date(value);       // already ms
    } else {
      date = new Date(value);
    }

    if (isNaN(date.getTime())) return '—';

    return new Intl.DateTimeFormat(undefined, {
      dateStyle: format === 'short' ? 'short' : 'medium',
      timeStyle: 'short'
    }).format(date);
  }
}
