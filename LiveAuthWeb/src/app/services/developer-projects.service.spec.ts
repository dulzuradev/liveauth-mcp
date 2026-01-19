import { TestBed } from '@angular/core/testing';

import { DeveloperProjects } from './developer-projects.service';

describe('DeveloperProjects', () => {
  let service: DeveloperProjects;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DeveloperProjects);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
