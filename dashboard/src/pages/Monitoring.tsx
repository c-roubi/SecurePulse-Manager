const Monitoring = () => {
  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Monitoring</h1>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <h2 className="text-xl font-bold text-white mb-4">Alerts</h2>
          <p className="text-gray-400">No alerts at the moment</p>
        </div>

        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <h2 className="text-xl font-bold text-white mb-4">Network Activity</h2>
          <p className="text-gray-400">Real-time monitoring coming soon</p>
        </div>
      </div>
    </div>
  )
}

export default Monitoring
