import { useState } from 'react'

const Commands = () => {
  const [command, setCommand] = useState('')
  const [output, setOutput] = useState('')

  const handleExecute = async (e: React.FormEvent) => {
    e.preventDefault()
    // TODO: Execute command via API
    setOutput(`Executed: ${command}`)
  }

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Commands</h1>

      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">Execute Command</h2>
        <form onSubmit={handleExecute} className="space-y-4">
          <input
            type="text"
            value={command}
            onChange={(e) => setCommand(e.target.value)}
            placeholder="Enter command..."
            className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded text-white"
          />
          <button
            type="submit"
            className="px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded"
          >
            Execute
          </button>
        </form>
      </div>

      {output && (
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <h2 className="text-xl font-bold text-white mb-4">Output</h2>
          <pre className="text-green-400 font-mono text-sm overflow-auto">
            {output}
          </pre>
        </div>
      )}
    </div>
  )
}

export default Commands
