import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { DeveloperProjectsComponent } from './developer-projects';
import { DevAuthService } from '../../../services/dev-auth.service';
import { DeveloperProjectsService } from '../../../services/developer-projects.service';

describe('DeveloperProjectsComponent', () => {
  let component: DeveloperProjectsComponent;
  let fixture: ComponentFixture<DeveloperProjectsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DeveloperProjectsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        {
          provide: DevAuthService,
          useValue: {
            getGitHubStatus: () => of({ enabled: false }),
            getToken: () => null,
            getApiUrl: () => 'http://localhost:5000',
            saveToken: () => undefined,
            clearToken: () => undefined
          }
        },
        {
          provide: DeveloperProjectsService,
          useValue: {
            listProjects: () => of({ projects: [] }),
            listMcpTools: () => of({ tools: [] })
          }
        }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DeveloperProjectsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should default to the projects workspace', () => {
    expect(component.isProjectsPage).toBeTrue();
  });
});
