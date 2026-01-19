import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminProjectsDonut } from './admin-projects-donut';

describe('AdminProjectsDonut', () => {
  let component: AdminProjectsDonut;
  let fixture: ComponentFixture<AdminProjectsDonut>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminProjectsDonut]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminProjectsDonut);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
