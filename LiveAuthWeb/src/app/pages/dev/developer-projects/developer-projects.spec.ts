import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DeveloperProjectsComponent } from './developer-projects';

describe('DeveloperProjectsComponent', () => {
  let component: DeveloperProjectsComponent;
  let fixture: ComponentFixture<DeveloperProjectsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DeveloperProjectsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DeveloperProjectsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
