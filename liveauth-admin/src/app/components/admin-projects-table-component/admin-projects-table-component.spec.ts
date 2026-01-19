import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminProjectsTableComponent } from './admin-projects-table-component';

describe('AdminProjectsTableComponent', () => {
  let component: AdminProjectsTableComponent;
  let fixture: ComponentFixture<AdminProjectsTableComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminProjectsTableComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminProjectsTableComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
