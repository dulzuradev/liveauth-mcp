import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { DeveloperProjectsService } from './developer-projects.service';

describe('DeveloperProjectsService', () => {
  let service: DeveloperProjectsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()]
    });
    service = TestBed.inject(DeveloperProjectsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
