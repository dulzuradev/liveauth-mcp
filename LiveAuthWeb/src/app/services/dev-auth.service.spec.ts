import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { DevAuthService } from './dev-auth.service';

describe('DevAuthService', () => {
  let service: DevAuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()]
    });
    service = TestBed.inject(DevAuthService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
