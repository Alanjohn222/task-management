import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import type { Priority, Task } from './TaskList'

type TaskItemProps = {
  task: Task
  accessToken: string
  onSessionExpired: () => void
  onUpdated: (task: Task) => void
  onDeleted: (id: string) => void
}

export default function TaskItem({
  task,
  accessToken,
  onSessionExpired,
  onUpdated,
  onDeleted,
}: TaskItemProps) {
  const [isEditing, setIsEditing] = useState(false)
  const [title, setTitle] = useState(task.title)
  const [description, setDescription] = useState(task.description ?? '')
  const [dueDate, setDueDate] = useState(task.dueDate ?? '')
  const [priority, setPriority] = useState<Priority>(task.priority)
  const [isCompleted, setIsCompleted] = useState(task.isCompleted)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [isBusy, setIsBusy] = useState(false)
  const [error, setError] = useState('')
  const requestScope = useRef<AbortController | null>(null)
  const mutationPending = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    requestScope.current = controller
    return () => controller.abort()
  }, [])

  function startEditing() {
    setTitle(task.title)
    setDescription(task.description ?? '')
    setDueDate(task.dueDate ?? '')
    setPriority(task.priority)
    setIsCompleted(task.isCompleted)
    setError('')
    setConfirmDelete(false)
    setIsEditing(true)
  }

  async function mutate(method: 'PUT' | 'DELETE', values?: Omit<Task, 'id'>) {
    const signal = requestScope.current?.signal
    if (!signal || signal.aborted || mutationPending.current) return

    mutationPending.current = true
    setIsBusy(true)
    setError('')

    try {
      const response = await fetch(`/api/tasks/${task.id}`, {
        method,
        headers: {
          Authorization: `Bearer ${accessToken}`,
          ...(method === 'PUT' ? { 'Content-Type': 'application/json' } : {}),
        },
        body: values ? JSON.stringify(values) : undefined,
        signal,
      })

      if (signal.aborted) return

      if (response.status === 401) {
        onSessionExpired()
        return
      }

      if (response.status === 404) {
        setError('This task is no longer available. Sign in again to reload your list.')
        return
      }

      if (!response.ok) {
        setError('Could not save your change. Please try again.')
        return
      }

      if (method === 'DELETE') {
        onDeleted(task.id)
      } else {
        const updated: Task = await response.json()
        if (signal.aborted) return
        onUpdated(updated)
        setIsEditing(false)
      }
    } catch {
      if (!signal.aborted) {
        setError('Could not confirm the change. Reload your list before retrying.')
      }
    } finally {
      mutationPending.current = false
      if (!signal.aborted) setIsBusy(false)
    }
  }

  function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmedTitle = title.trim()
    if (!trimmedTitle) {
      setError('Enter a task title.')
      return
    }
    void mutate('PUT', {
      title: trimmedTitle,
      description: description.trim() || null,
      isCompleted,
      dueDate: dueDate || null,
      priority,
    })
  }

  return (
    <li className="task-row">
      <div className="task-content">
        {isEditing ? (
          <form onSubmit={handleSave}>
            <label>
              Title
              <input
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                maxLength={200}
                required
                disabled={isBusy}
                autoFocus
              />
            </label>
            <label>
              Description
              <textarea
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                maxLength={2000}
                disabled={isBusy}
              />
            </label>
            <div className="task-fields">
              <label>
                Due date (optional)
                <input
                  type="date"
                  min="0001-01-01"
                  max="9999-12-31"
                  value={dueDate}
                  onChange={(event) => setDueDate(event.target.value)}
                  disabled={isBusy}
                />
              </label>
              <label>
                <span id={`priority-label-${task.id}`}>Priority</span>
                <select
                  aria-labelledby={`priority-label-${task.id}`}
                  value={priority}
                  onChange={(event) => setPriority(event.target.value as Priority)}
                  disabled={isBusy}
                >
                  <option value="Low">Low</option>
                  <option value="Medium">Medium</option>
                  <option value="High">High</option>
                </select>
              </label>
            </div>
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={isCompleted}
                onChange={(event) => setIsCompleted(event.target.checked)}
                disabled={isBusy}
              />
              Completed
            </label>
            <div className="task-actions">
              <button disabled={isBusy}>{isBusy ? 'Saving…' : 'Save changes'}</button>
              <button
                type="button"
                className="secondary"
                disabled={isBusy}
                onClick={() => { setIsEditing(false); setError('') }}
              >
                Cancel
              </button>
            </div>
          </form>
        ) : (
          <>
            <div className="task-heading">
              <strong>{task.title}</strong>
              <span className={task.isCompleted ? 'status complete' : 'status'}>
                {task.isCompleted ? 'Complete' : 'Open'}
              </span>
            </div>
            {task.description && <p className="muted">{task.description}</p>}
            <div className="task-metadata">
              <span className={`priority priority-${task.priority.toLowerCase()}`}>
                {task.priority} priority
              </span>
              <span className="muted">
                {task.dueDate
                  ? <>Due <time dateTime={task.dueDate}>{task.dueDate}</time></>
                  : 'No due date'}
              </span>
            </div>
            {confirmDelete ? (
              <div className="delete-confirmation">
                <p>Delete this task permanently?</p>
                <div className="task-actions">
                  <button className="danger" disabled={isBusy} onClick={() => void mutate('DELETE')}>
                    {isBusy ? 'Deleting…' : 'Delete task'}
                  </button>
                  <button className="secondary" disabled={isBusy} onClick={() => setConfirmDelete(false)}>
                    Keep task
                  </button>
                </div>
              </div>
            ) : (
              <div className="task-actions">
                <button
                  className="secondary"
                  disabled={isBusy}
                  onClick={() => void mutate('PUT', {
                    title: task.title,
                    description: task.description,
                    dueDate: task.dueDate,
                    priority: task.priority,
                    isCompleted: !task.isCompleted,
                  })}
                >
                  {isBusy ? 'Saving…' : task.isCompleted ? 'Reopen' : 'Mark complete'}
                </button>
                <button className="secondary" disabled={isBusy} onClick={startEditing}>Edit</button>
                <button className="danger secondary" disabled={isBusy} onClick={() => setConfirmDelete(true)}>
                  Delete
                </button>
              </div>
            )}
          </>
        )}
        {error && <p role="alert">{error}</p>}
      </div>
    </li>
  )
}
