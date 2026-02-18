import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DragDropModule } from 'primeng/dragdrop';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { DialogModule } from 'primeng/dialog';
import { TagModule } from 'primeng/tag';
import { DatePickerModule } from 'primeng/datepicker';
import { MissionControlService, MissionTask, TaskStatus, TaskType, TaskPriority, CreateTaskDto, TaskFilter } from '../../services/mission-control.service';

interface KanbanColumn {
  id: TaskStatus;
  title: string;
  tasks: MissionTask[];
}

@Component({
  selector: 'app-kanban-board',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    DragDropModule,
    ButtonModule,
    InputTextModule,
    TextareaModule,
    SelectModule,
    DialogModule,
    TagModule,
    DatePickerModule
  ],
  template: `
    <div class="kanban-container">
      <div class="kanban-header">
        <h1>Mission Control</h1>
        <div class="filters">
          <p-select
            [options]="typeOptions"
            [(ngModel)]="filterType"
            placeholder="All Types"
            [showClear]="true"
            (onChange)="loadBoard()"
            styleClass="filter-select">
          </p-select>
          <p-select
            [options]="priorityOptions"
            [(ngModel)]="filterPriority"
            placeholder="All Priorities"
            [showClear]="true"
            (onChange)="loadBoard()"
            styleClass="filter-select">
          </p-select>
        </div>
        <button pButton label="+ Add Task" (click)="openAddDialog()" class="add-btn"></button>
      </div>

      <div class="kanban-board">
        @for (column of columns; track column.id) {
          <div class="kanban-column" 
               pDroppable="tasks" 
               (onDrop)="onDrop($event, column.id)">
            <div class="column-header" [attr.data-status]="column.id">
              <span class="column-title">{{ column.title }}</span>
              <span class="task-count">{{ column.tasks.length }}</span>
            </div>
            <div class="column-tasks">
              @for (task of column.tasks; track task.id) {
                <div class="task-card" 
                     pDraggable="tasks" 
                     (onDragStart)="onDragStart(task)"
                     (click)="openEditDialog(task)">
                  <div class="task-header">
                    <p-tag [value]="task.type" [severity]="getTypeSeverity(task.type)" styleClass="task-type"></p-tag>
                    <p-tag [value]="task.priority" [severity]="getPrioritySeverity(task.priority)" styleClass="task-priority"></p-tag>
                  </div>
                  <h4 class="task-title">{{ task.title }}</h4>
                  @if (task.description) {
                    <p class="task-desc">{{ task.description }}</p>
                  }
                  <div class="task-meta">
                    @if (task.owner) {
                      <span class="task-owner">👤 {{ task.owner }}</span>
                    }
                    @if (task.dueDate) {
                      <span class="task-due" [class.overdue]="isOverdue(task.dueDate)">📅 {{ formatDate(task.dueDate) }}</span>
                    }
                  </div>
                </div>
              }
            </div>
          </div>
        }
      </div>

      <!-- Add/Edit Dialog -->
      <p-dialog 
        [(visible)]="showDialog" 
        [header]="editingTask ? 'Edit Task' : 'New Task'" 
        [modal]="true" 
        [style]="{width: '500px'}"
        styleClass="task-dialog">
        <div class="dialog-content">
          <div class="form-field">
            <label>Title *</label>
            <input pInputText [(ngModel)]="taskForm.title" placeholder="Task title" class="full-width" />
          </div>
          <div class="form-field">
            <label>Description</label>
            <textarea pInputTextarea [(ngModel)]="taskForm.description" rows="3" placeholder="Task description" class="full-width"></textarea>
          </div>
          <div class="form-row">
            <div class="form-field">
              <label>Type</label>
              <p-select [options]="typeOptions" [(ngModel)]="taskForm.type" placeholder="Select type" styleClass="full-width"></p-select>
            </div>
            <div class="form-field">
              <label>Priority</label>
              <p-select [options]="priorityOptions" [(ngModel)]="taskForm.priority" placeholder="Select priority" styleClass="full-width"></p-select>
            </div>
          </div>
          <div class="form-row">
            <div class="form-field">
              <label>Owner</label>
              <input pInputText [(ngModel)]="taskForm.owner" placeholder="Assignee" class="full-width" />
            </div>
            <div class="form-field">
              <label>Due Date</label>
              <p-datepicker [(ngModel)]="taskForm.dueDate" dateFormat="yy-mm-dd" [showIcon]="true" styleClass="full-width"></p-datepicker>
            </div>
          </div>
          @if (editingTask) {
            <div class="form-field">
              <label>Status</label>
              <p-select [options]="statusOptions" [(ngModel)]="taskForm.status" placeholder="Select status" styleClass="full-width"></p-select>
            </div>
          }
        </div>
        <ng-template pTemplate="footer">
          @if (editingTask) {
            <button pButton label="Delete" severity="danger" (click)="deleteTask()" class="p-button-text"></button>
          }
          <button pButton label="Cancel" (click)="showDialog = false" class="p-button-text"></button>
          <button pButton [label]="editingTask ? 'Save' : 'Create'" (click)="saveTask()"></button>
        </ng-template>
      </p-dialog>
    </div>
  `,
  styles: [`
    .kanban-container {
      padding: 24px;
      min-height: 100vh;
    }

    .kanban-header {
      display: flex;
      align-items: center;
      gap: 24px;
      margin-bottom: 24px;
      flex-wrap: wrap;
    }

    .kanban-header h1 {
      margin: 0;
      font-size: 1.75rem;
      color: var(--electric-blue);
    }

    .filters {
      display: flex;
      gap: 12px;
      flex: 1;
    }

    .filter-select {
      min-width: 150px;
    }

    .add-btn {
      background: var(--btc-orange);
      border: none;
      color: #121212;
      font-weight: 600;
    }

    .add-btn:hover {
      background: #e09a00;
    }

    .kanban-board {
      display: flex;
      gap: 16px;
      overflow-x: auto;
      padding-bottom: 16px;
    }

    .kanban-column {
      flex: 0 0 280px;
      background: rgba(17, 24, 45, 0.6);
      border-radius: 12px;
      padding: 12px;
      min-height: 500px;
      border: 1px solid rgba(255, 255, 255, 0.05);
    }

    .column-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px;
      margin-bottom: 12px;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.03);
    }

    .column-header[data-status="Backlog"] { border-left: 3px solid #94a3b8; }
    .column-header[data-status="Ready"] { border-left: 3px solid #3b82f6; }
    .column-header[data-status="InProgress"] { border-left: 3px solid #f59e0b; }
    .column-header[data-status="Blocked"] { border-left: 3px solid #ef4444; }
    .column-header[data-status="Review"] { border-left: 3px solid #8b5cf6; }
    .column-header[data-status="Done"] { border-left: 3px solid #22c55e; }

    .column-title {
      font-weight: 600;
      font-size: 0.9rem;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .task-count {
      background: rgba(255, 255, 255, 0.1);
      padding: 2px 8px;
      border-radius: 12px;
      font-size: 0.75rem;
    }

    .column-tasks {
      display: flex;
      flex-direction: column;
      gap: 10px;
      min-height: 100px;
    }

    .task-card {
      background: rgba(255, 255, 255, 0.05);
      border-radius: 8px;
      padding: 12px;
      cursor: grab;
      border: 1px solid rgba(255, 255, 255, 0.05);
      transition: all 0.2s ease;
    }

    .task-card:hover {
      background: rgba(255, 255, 255, 0.08);
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
    }

    .task-card:active {
      cursor: grabbing;
    }

    .task-header {
      display: flex;
      gap: 6px;
      margin-bottom: 8px;
    }

    .task-type, .task-priority {
      font-size: 0.65rem;
      padding: 2px 6px;
    }

    .task-title {
      margin: 0 0 6px 0;
      font-size: 0.95rem;
      color: #e3e7ee;
      line-height: 1.3;
    }

    .task-desc {
      margin: 0 0 8px 0;
      font-size: 0.8rem;
      color: #94a3b8;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .task-meta {
      display: flex;
      gap: 12px;
      font-size: 0.75rem;
      color: #64748b;
    }

    .task-due.overdue {
      color: #ef4444;
    }

    /* Dialog Styles */
    .dialog-content {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .form-field {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-field label {
      font-size: 0.85rem;
      color: #94a3b8;
      font-weight: 500;
    }

    .form-row {
      display: flex;
      gap: 16px;
    }

    .form-row .form-field {
      flex: 1;
    }

    .full-width {
      width: 100%;
    }

    :host ::ng-deep .p-dialog {
      background: var(--wall-surface);
      border: 1px solid rgba(255, 255, 255, 0.1);
    }

    :host ::ng-deep .p-dialog-header {
      background: transparent;
      border-bottom: 1px solid rgba(255, 255, 255, 0.1);
    }

    :host ::ng-deep .p-dialog-content {
      background: transparent;
    }

    :host ::ng-deep .p-dialog-footer {
      background: transparent;
      border-top: 1px solid rgba(255, 255, 255, 0.1);
    }
  `]
})
export class KanbanBoardComponent implements OnInit {
  columns: KanbanColumn[] = [
    { id: 'Backlog', title: 'Backlog', tasks: [] },
    { id: 'Ready', title: 'Ready', tasks: [] },
    { id: 'InProgress', title: 'In Progress', tasks: [] },
    { id: 'Blocked', title: 'Blocked', tasks: [] },
    { id: 'Review', title: 'Review', tasks: [] },
    { id: 'Done', title: 'Done', tasks: [] }
  ];

  typeOptions = [
    { label: 'Feature', value: 'Feature' },
    { label: 'Bug', value: 'Bug' },
    { label: 'Infra', value: 'Infra' },
    { label: 'Research', value: 'Research' }
  ];

  priorityOptions = [
    { label: 'Low', value: 'Low' },
    { label: 'Medium', value: 'Medium' },
    { label: 'High', value: 'High' },
    { label: 'Critical', value: 'Critical' }
  ];

  statusOptions = this.columns.map(c => ({ label: c.title, value: c.id }));

  filterType: TaskType | null = null;
  filterPriority: TaskPriority | null = null;

  showDialog = false;
  editingTask: MissionTask | null = null;
  draggedTask: MissionTask | null = null;

  taskForm: CreateTaskDto & { status?: TaskStatus } = {
    title: '',
    description: '',
    type: 'Feature',
    priority: 'Medium',
    owner: '',
    dueDate: undefined
  };

  constructor(private missionService: MissionControlService) {}

  ngOnInit() {
    this.loadBoard();
  }

  loadBoard() {
    const filter: TaskFilter = {};
    if (this.filterType) filter.type = this.filterType;
    if (this.filterPriority) filter.priority = this.filterPriority;

    this.missionService.getKanban(filter).subscribe({
      next: (data: Record<TaskStatus, MissionTask[]>) => {
        this.columns = this.columns.map(col => ({
          ...col,
          tasks: data[col.id] || []
        }));
      },
      error: (err: any) => console.error('Failed to load kanban:', err)
    });
  }

  onDragStart(task: MissionTask) {
    this.draggedTask = task;
  }

  onDrop(event: any, targetStatus: TaskStatus) {
    if (this.draggedTask && this.draggedTask.status !== targetStatus) {
      this.missionService.moveTask(this.draggedTask.id, targetStatus).subscribe({
        next: () => {
          this.draggedTask!.status = targetStatus;
          this.loadBoard();
        },
        error: (err: any) => console.error('Failed to move task:', err)
      });
    }
    this.draggedTask = null;
  }

  openAddDialog() {
    this.editingTask = null;
    this.taskForm = {
      title: '',
      description: '',
      type: 'Feature',
      priority: 'Medium',
      owner: '',
      dueDate: undefined
    };
    this.showDialog = true;
  }

  openEditDialog(task: MissionTask) {
    this.editingTask = task;
    this.taskForm = {
      title: task.title,
      description: task.description || '',
      type: task.type,
      priority: task.priority,
      owner: task.owner || '',
      status: task.status,
      dueDate: task.dueDate ? new Date(task.dueDate) : undefined
    };
    this.showDialog = true;
  }

  saveTask() {
    if (!this.taskForm.title?.trim()) return;

    // Convert Date to ISO string for API
    const dto: any = { ...this.taskForm };
    if (dto.dueDate instanceof Date) {
      dto.dueDate = dto.dueDate.toISOString();
    }

    if (this.editingTask) {
      this.missionService.updateTask(this.editingTask.id, dto).subscribe({
        next: () => {
          this.showDialog = false;
          this.loadBoard();
        },
        error: (err: any) => console.error('Failed to update task:', err)
      });
    } else {
      this.missionService.createTask(dto).subscribe({
        next: () => {
          this.showDialog = false;
          this.loadBoard();
        },
        error: (err: any) => console.error('Failed to create task:', err)
      });
    }
  }

  deleteTask() {
    if (!this.editingTask) return;
    if (!confirm('Delete this task?')) return;

    this.missionService.deleteTask(this.editingTask.id).subscribe({
      next: () => {
        this.showDialog = false;
        this.loadBoard();
      },
      error: (err: any) => console.error('Failed to delete task:', err)
    });
  }

  getTypeSeverity(type: TaskType): "success" | "info" | "warn" | "danger" | "secondary" | "contrast" | undefined {
    switch (type) {
      case 'Feature': return 'success';
      case 'Bug': return 'danger';
      case 'Infra': return 'warn';
      case 'Research': return 'info';
      default: return 'secondary';
    }
  }

  getPrioritySeverity(priority: TaskPriority): "success" | "info" | "warn" | "danger" | "secondary" | "contrast" | undefined {
    switch (priority) {
      case 'Critical': return 'danger';
      case 'High': return 'warn';
      case 'Medium': return 'info';
      case 'Low': return 'secondary';
      default: return 'secondary';
    }
  }

  isOverdue(dateStr: string): boolean {
    return new Date(dateStr) < new Date();
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }
}
