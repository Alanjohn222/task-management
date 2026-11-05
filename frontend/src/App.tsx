import { useCallback, useState } from 'react'
import type { FormEvent } from 'react'
import TaskList from './TaskList'

type LoginResponse = {
  accessToken: string
  expiresAt: string
  name: string | null
}

export default function App() {
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [session, setSession] = useState<LoginResponse | null>(null)
  const [error, setError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isRegistering, setIsRegistering] = useState(false)
  const [notice, setNotice] = useState('')

  const handleSessionExpired = useCallback(() => {
    setSession(null)
    setError('Your session expired. Please sign in again.')
  }, [])

  function switchAuthMode() {
    setIsRegistering((current) => !current)
    setPassword('')
    setError('')
    setNotice('')
  }

  async function handleAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (isSubmitting) return
    setError('')
    setNotice('')
    if (isRegistering && !name.trim()) {
      setError('Enter your name.')
      return
    }
    setIsSubmitting(true)

    try {
      const response = await fetch(isRegistering ? '/api/auth/register' : '/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...(isRegistering ? { name: name.trim() } : {}),
          email: email.trim(),
          password,
        }),
      })

      if (response.status === 401) {
        setError('Invalid email or password.')
        return
      }

      if (response.status === 409 && isRegistering) {
        setError('An account with this email already exists. Try signing in.')
        return
      }

      if (response.status === 400) {
        const problem: { errors?: Record<string, string[]> } = await response.json()
        const messages = Object.values(problem.errors ?? {}).flat()
        setError(messages.join(' ') || 'Check the form fields and try again.')
        return
      }

      if (!response.ok) {
        setError(isRegistering
          ? 'Could not create your account. Please try again.'
          : 'Login failed. Check your details and try again.')
        return
      }

      if (isRegistering) {
        setIsRegistering(false)
        setName('')
        setPassword('')
        setNotice('Account created. Sign in with your new password.')
        return
      }

      const result: LoginResponse = await response.json()
      setSession(result)
      setPassword('')
    } catch {
      setError('Could not reach the API. Try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  if (session) {
    return (
      <main className="workspace">
        <header className="workspace-header">
          <div>
            <p className="eyebrow">Task Management</p>
            <h1>{session.name ? `Welcome, ${session.name}` : 'My tasks'}</h1>
            <p className="muted">{email}</p>
          </div>
          <button className="secondary" onClick={() => setSession(null)}>Sign out</button>
        </header>
        <TaskList
          accessToken={session.accessToken}
          onSessionExpired={handleSessionExpired}
        />
      </main>
    )
  }

  return (
    <main className="login-page">
      <section className="panel login-card">
        <p className="eyebrow">Task Management</p>
        <h1>{isRegistering ? 'Create account' : 'Sign in'}</h1>
        <p className="muted">
          {isRegistering
            ? 'Create an account to start managing your tasks.'
            : 'Enter your email and password to view your tasks.'}
        </p>
        {notice && <p className="success-message" role="status">{notice}</p>}
        <form onSubmit={handleAuth}>
          {isRegistering && (
            <p>
              <label>
                Name
                <input
                  type="text"
                  autoComplete="name"
                  maxLength={100}
                  disabled={isSubmitting}
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  required
                />
              </label>
            </p>
          )}
          <p>
            <label>
              Email
              <input
                type="email"
                autoComplete="username"
                maxLength={254}
                disabled={isSubmitting}
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                required
              />
            </label>
          </p>
          <p>
            <label>
              Password
              <input
                type="password"
                autoComplete={isRegistering ? 'new-password' : 'current-password'}
                minLength={isRegistering ? 6 : undefined}
                maxLength={128}
                disabled={isSubmitting}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </label>
          </p>
          {isRegistering && <p className="muted password-hint">Use a password of 6–128 characters.</p>}
          {error && <p role="alert">{error}</p>}
          <button disabled={isSubmitting}>
            {isSubmitting
              ? isRegistering ? 'Creating account…' : 'Signing in…'
              : isRegistering ? 'Create account' : 'Log in'}
          </button>
        </form>
        <p className="auth-switch">
          {isRegistering ? 'Already have an account?' : 'New here?'}{' '}
          <button type="button" className="text-button" disabled={isSubmitting} onClick={switchAuthMode}>
            {isRegistering ? 'Sign in' : 'Create account'}
          </button>
        </p>
      </section>
    </main>
  )
}
