import { TestBed } from '@angular/core/testing';

import { AdminAnalytics } from './admin-analytics';

describe('AdminAnalytics', () => {
  let service: AdminAnalytics;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(AdminAnalytics);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
