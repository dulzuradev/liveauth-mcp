import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { LiveAuthClientService } from './liveauth-client.service';

describe('LiveAuthClientService', () => {
  let service: LiveAuthClientService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()]
    });
    service = TestBed.inject(LiveAuthClientService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
