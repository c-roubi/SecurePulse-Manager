import { useState, useEffect } from 'react'
import { apiClient } from '../services/api'

interface Session {
  id: number
  agent_id: number
  user_id: number
  session_token: string
  started_at: string
  ended_at: string | null
  status: string
  command_count: number
  last_activity: string
}

const Sessions = () => {
  const [sessions, setSessions] = useState<Session[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const loadSessions = async () => {
      try {
        const data = await apiClient.getSessions()
        setSessions(data)
      } catch (err) {
        console.error('Failed to load sessions:', err)
      } finally {
        setLoading(false)
      }
    }

    loadSessions()
  }, [])

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Sessions</h1>

      <div className="bg-gray-800 rounded-lg border border-gray-700 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-700">
              <tr>
                <th className="px-6 py-3 text-left text-sm font-medium text-gray-300">ID</th>
                <th className="px-6 py-3 text-left text-sm font-medium text-gray-300">Agent ID</th>
                <th className="px-6 py-3 text-left text-sm font-medium text-gray-300">Status</th>
                <th className="px-6 py-3 text-left text-sm font-medium text-gray-300">Commands</th>
                <th className="px-6 py-3 text-left text-sm font-medium text-gray-300">Started</th>
                <th className="px-6 py-3 text-left text-sm font-medium text-gray-300">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-700">
              {loading ? (
                <tr>
                  <td colSpan={6} className="px-6 py-4 text-center text-gray-400">
                    Loading...
                  </td>
                </tr>
              ) : sessions.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-6 py-4 text-center text-gray-400">
                    No sessions found
                  </td>
                </tr>
              ) : (
                sessions.map((session) => (
                  <tr key={session.id} className="hover:bg-gray-700 transition-colors">
                    <td className="px-6 py-4 text-sm text-white">#{session.id}</td>
                    <td className="px-6 py-4 text-sm text-gray-300">{session.agent_id}</td>
                    <td className="px-6 py-4 text-sm">
                      <span
                        className={`px-2 py-1 rounded text-xs font-medium ${
                          session.status === 'ACTIVE'
                            ? 'bg-green-900 text-green-200'
                            : 'bg-gray-700 text-gray-200'
                        }`}
                      >
                        {session.status}
                      </span>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-300">{session.command_count}</td>
                    <td className="px-6 py-4 text-sm text-gray-300">
                      {new Date(session.started_at).toLocaleString()}
                    </td>
                    <td className="px-6 py-4 text-sm">
                      <button className="text-blue-400 hover:text-blue-300 mr-3">View</button>
                      <button className="text-red-400 hover:text-red-300">Close</button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

export default Sessions
