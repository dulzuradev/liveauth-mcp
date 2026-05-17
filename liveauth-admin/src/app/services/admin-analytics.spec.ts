import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { AdminAnalyticsService } from './admin-analytics';

describe('AdminAnalyticsService', () => {
  let service: AdminAnalyticsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()]
    });
    service = TestBed.inject(AdminAnalyticsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
