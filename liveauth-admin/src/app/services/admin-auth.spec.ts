import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { AdminAuthService } from './admin-auth';

describe('AdminAuthService', () => {
  let service: AdminAuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()]
    });
    service = TestBed.inject(AdminAuthService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
