import { TestBed } from '@angular/core/testing';

import { LiveauthClient } from './liveauth-client.service';

describe('LiveauthClient', () => {
  let service: LiveauthClient;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(LiveauthClient);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
