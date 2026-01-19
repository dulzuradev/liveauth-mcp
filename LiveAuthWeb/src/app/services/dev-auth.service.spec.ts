import { TestBed } from '@angular/core/testing';

import { DevAuth } from './dev-auth.service';

describe('DevAuth', () => {
  let service: DevAuth;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DevAuth);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
