import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminProjectsDonutComponent } from './admin-projects-donut';

describe('AdminProjectsDonutComponent', () => {
  let component: AdminProjectsDonutComponent;
  let fixture: ComponentFixture<AdminProjectsDonutComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminProjectsDonutComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminProjectsDonutComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
