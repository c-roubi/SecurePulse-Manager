import { Link, useLocation } from 'react-router-dom'

const Sidebar = () => {
  const location = useLocation()

  const menuItems = [
    { path: '/dashboard', label: '📊 Dashboard', icon: '📊' },
    { path: '/sessions', label: '🔗 Sessions', icon: '🔗' },
    { path: '/commands', label: '⌨️  Commands', icon: '⌨️' },
    { path: '/monitoring', label: '📡 Monitoring', icon: '📡' },
    { path: '/files', label: '🗂️  File Manager', icon: '🗂️' },
    { path: '/remote-access', label: '🖥️  Remote Desktop', icon: '🖥️' },
    { path: '/keylogger', label: '🔐 Keylogger', icon: '🔐' },
    { path: '/password-manager', label: '🔑 Password Manager', icon: '🔑' },
  ]

  const isActive = (path: string) => {
    if (path === '/files') {
      return location.pathname.startsWith('/files')
    }
    if (path === '/remote-access') {
      return location.pathname.startsWith('/remote-access')
    }
    if (path === '/password-manager') {
      return location.pathname.startsWith('/password-manager')
    }
    return location.pathname === path || (path === '/dashboard' && location.pathname === '/')
  }

  return (
    <aside className="w-64 bg-gray-800 border-r border-gray-700 p-4">
      {/* Logo */}
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-blue-500 flex items-center gap-2">
          🛡️ C2 Panel
        </h1>
        <p className="text-gray-400 text-sm mt-2">Remote Administration</p>
      </div>

      {/* Navigation */}
      <nav className="space-y-2">
        {menuItems.map((item) => (
          <Link
            key={item.path}
            to={item.path}
            className={`block px-4 py-3 rounded-lg transition-colors ${
              isActive(item.path)
                ? 'bg-blue-600 text-white'
                : 'text-gray-300 hover:bg-gray-700'
            }`}
          >
            {item.label}
          </Link>
        ))}
      </nav>

      {/* Bottom Section */}
      <div className="absolute bottom-4 left-4 right-4 border-t border-gray-700 pt-4">
        <div className="text-xs text-gray-400">
          <p>👤 Operator</p>
          <p className="mt-1">v0.1.0</p>
        </div>
      </div>
    </aside>
  )
}

export default Sidebar
