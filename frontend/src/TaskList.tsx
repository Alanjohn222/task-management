import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import TaskItem from './TaskItem'

export type Priority = 'Low' | 'Medium' | 'High'

export type Task = {
  id: string
  title: string
  description: string | null
  isCompleted: boolean
  dueDate: string | null
  priority: Priority
}

type TaskPage = {
  items: Task[]
  page: number
  pageSize: number
  totalCount: number
}

type ListQuery = {
  search: string
  status: '' | 'true' | 'false'
  priority: '' | Priority
  page: number
}

type TaskListProps = {
  accessToken: string
  onSessionExpired: () => void
}

export default function TaskList({
  accessToken,
  onSessionExpired,
}: TaskListProps) {
  const [query, setQuery] = useState<ListQuery>({
    search: '', status: '', priority: '', page: 1,
  })
  const [searchInput, setSearchInput] = useState('')
  const [reloadVersion, setReloadVersion] = useState(0)
  const [listResult, setListResult] = useState<{
    query: ListQuery
    version: number
    data?: TaskPage
    error?: string
  } | null>(null)
  const isLoading = listResult?.query !== query || listResult?.version !== reloadVersion
  const taskPage = listResult?.data ?? null
  const error = listResult?.error ?? ''
  const [createNotice, setCreateNotice] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [priority, setPriority] = useState<Priority>('Medium')
  const [createError, setCreateError] = useState('')
  const [isCreating, setIsCreating] = useState(false)
  const requestScope = useRef<AbortController | null>(null)
  const createPending = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    requestScope.current = controller
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const parameters = new URLSearchParams({
      page: String(query.page),
      pageSize: '20',
    })
    if (query.search) parameters.set('search', query.search)
    if (query.status) parameters.set('isCompleted', query.status)
    if (query.priority) parameters.set('priority', query.priority)

    async function loadTasks() {
      try {
        const response = await fetch(`/api/tasks?${parameters}`, {
          headers: { Authorization: `Bearer ${accessToken}` },
          signal: controller.signal,
        })

        if (controller.signal.aborted) return
        if (response.status === 401) {
          onSessionExpired()
          return
        }

        if (!response.ok) {
          setListResult({ query, version: reloadVersion, error: 'Could not load your tasks.' })
          return
        }

        const result: TaskPage = await response.json()
        if (controller.signal.aborted) return

        if (result.items.length === 0 && query.page > 1) {
          const lastPage = Math.max(1, Math.ceil(result.totalCount / result.pageSize))
          setQuery((current) => ({ ...current, page: Math.min(current.page - 1, lastPage) }))
          return
        }

        setListResult({ query, version: reloadVersion, data: result })
      } catch {
        if (!controller.signal.aborted) {
          setListResult({ query, version: reloadVersion, error: 'Could not reach the API. Try again.' })
        }
      }
    }

    void loadTasks()
    return () => controller.abort()
  }, [accessToken, onSessionExpired, query, reloadVersion])

  function reloadTasks() {
    setReloadVersion((current) => current + 1)
  }

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setQuery((current) => ({ ...current, search: searchInput.trim(), page: 1 }))
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setCreateError('')
    setCreateNotice('')

    const trimmedTitle = title.trim()
    if (!trimmedTitle) {
      setCreateError('Enter a task title.')
      return
    }

    const signal = requestScope.current?.signal
    if (!signal || signal.aborted || createPending.current) return

    createPending.current = true

    setIsCreating(true)

    try {
      const response = await fetch('/api/tasks', {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${accessToken}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          title: trimmedTitle,
          description: description.trim() || null,
          dueDate: dueDate || null,
          priority,
        }),
        signal,
      })

      if (signal.aborted) return
      if (response.status === 401) {
        onSessionExpired()
        return
      }

      if (!response.ok) {
        setCreateError('Could not create the task. Try again.')
        return
      }

      setCreateNotice('Task added.')
      reloadTasks()
      setTitle('')
      setDescription('')
      setDueDate('')
      setPriority('Medium')
    } catch {
      if (!signal.aborted) {
        setCreateError('Could not confirm the task was created. Check your task list before retrying.')
      }
    } finally {
      createPending.current = false
      if (!signal.aborted) setIsCreating(false)
    }
  }

  return (
    <div className="task-layout">
      <form className="panel task-form" onSubmit={handleCreate}>
        <h2>New task</h2>
        <p>
          <label>
            Title
            <input
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              maxLength={200}
              required
              disabled={isCreating}
            />
          </label>
        </p>
        <p>
          <label>
            <span>Description <span className="muted">(optional)</span></span>
            <textarea
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              maxLength={2000}
              disabled={isCreating}
            />
          </label>
        </p>
        <div className="task-fields">
          <label>
            Due date (optional)
            <input
              type="date"
              min="0001-01-01"
              max="9999-12-31"
              value={dueDate}
              onChange={(event) => setDueDate(event.target.value)}
              disabled={isCreating}
            />
          </label>
          <label>
            <span id="new-task-priority-label">Priority</span>
            <select
              aria-labelledby="new-task-priority-label"
              value={priority}
              onChange={(event) => setPriority(event.target.value as Priority)}
              disabled={isCreating}
            >
              <option value="Low">Low</option>
              <option value="Medium">Medium</option>
              <option value="High">High</option>
            </select>
          </label>
        </div>
        {createNotice && <p role="status">{createNotice}</p>}
        {createError && <p role="alert">{createError}</p>}
        <button disabled={isCreating}>
          {isCreating ? 'Adding…' : 'Add task'}
        </button>
      </form>

      <section className="panel task-panel" aria-labelledby="task-list-title">
        <header className="list-header">
          <h2 id="task-list-title">Task list</h2>
          {!isLoading && !error && taskPage && (
            <span className="task-count">{taskPage.totalCount}</span>
          )}
        </header>
        <form className="list-controls" onSubmit={handleSearch}>
          <label>
            Search tasks
            <input
              type="search"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              placeholder="Search title or description"
            />
          </label>
          <button type="submit">Search</button>
        </form>
        <div className="list-filters">
          <label>
            <span id="task-status-filter-label">Status</span>
            <select
              aria-labelledby="task-status-filter-label"
              value={query.status}
              onChange={(event) => setQuery((current) => ({
                ...current, status: event.target.value as ListQuery['status'], page: 1,
              }))}
            >
              <option value="">All statuses</option>
              <option value="false">Open</option>
              <option value="true">Complete</option>
            </select>
          </label>
          <label>
            <span id="task-priority-filter-label">Filter priority</span>
            <select
              aria-labelledby="task-priority-filter-label"
              value={query.priority}
              onChange={(event) => setQuery((current) => ({
                ...current, priority: event.target.value as ListQuery['priority'], page: 1,
              }))}
            >
              <option value="">All priorities</option>
              <option value="Low">Low</option>
              <option value="Medium">Medium</option>
              <option value="High">High</option>
            </select>
          </label>
        </div>

        {isLoading ? (
          <p className="list-message" role="status">Loading tasks…</p>
        ) : error ? (
          <div className="list-message">
            <p role="alert">{error}</p>
            <button className="secondary" onClick={reloadTasks}>Retry</button>
          </div>
        ) : taskPage && (
          <>
            {taskPage.items.length === 0 ? (
              <div className="empty-state">
                <strong>No matching tasks</strong>
                <p className="muted">Add a task or change your search and filters.</p>
              </div>
            ) : (
              <ul className="task-list">
                {taskPage.items.map((task) => (
                  <TaskItem
                    key={task.id}
                    task={task}
                    accessToken={accessToken}
                    onSessionExpired={onSessionExpired}
                    onUpdated={reloadTasks}
                    onDeleted={reloadTasks}
                  />
                ))}
              </ul>
            )}
            <nav className="pagination" aria-label="Task pages">
              <button
                className="secondary"
                disabled={taskPage.page <= 1}
                onClick={() => setQuery((current) => ({ ...current, page: current.page - 1 }))}
              >
                Previous
              </button>
              <p role="status">
                Page {taskPage.page} of {Math.max(1, Math.ceil(taskPage.totalCount / taskPage.pageSize))}
                {' · '}{taskPage.totalCount} {taskPage.totalCount === 1 ? 'task' : 'tasks'}
              </p>
              <button
                className="secondary"
                disabled={taskPage.page * taskPage.pageSize >= taskPage.totalCount}
                onClick={() => setQuery((current) => ({ ...current, page: current.page + 1 }))}
              >
                Next
              </button>
            </nav>
          </>
        )}
      </section>
    </div>
  )
}
