import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DeveloperProjects } from './developer-projects';

describe('DeveloperProjects', () => {
  let component: DeveloperProjects;
  let fixture: ComponentFixture<DeveloperProjects>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DeveloperProjects]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DeveloperProjects);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
