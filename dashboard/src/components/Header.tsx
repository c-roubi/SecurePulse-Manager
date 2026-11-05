import { useAuthStore } from '../store/authStore'

const Header = () => {
  const { user, logout } = useAuthStore()

  const handleLogout = () => {
    logout()
    window.location.href = '/login'
  }

  return (
    <header className="bg-gray-800 border-b border-gray-700 px-6 py-4">
      <div className="flex justify-between items-center">
        <h2 className="text-xl font-semibold text-white">
          Control Panel
        </h2>

        <div className="flex items-center gap-4">
          {/* Status Indicator */}
          <div className="flex items-center gap-2">
            <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></div>
            <span className="text-sm text-gray-300">Connected</span>
          </div>

          {/* User Menu */}
          <div className="flex items-center gap-3 border-l border-gray-700 pl-4">
            <div>
              <p className="text-sm font-medium text-white">{user?.username || 'User'}</p>
              <p className="text-xs text-gray-400">{user?.role || 'operator'}</p>
            </div>
            <button
              onClick={handleLogout}
              className="px-3 py-1 text-sm bg-red-600 hover:bg-red-700 text-white rounded transition-colors"
            >
              Logout
            </button>
          </div>
        </div>
      </div>
    </header>
  )
}

export default Header
