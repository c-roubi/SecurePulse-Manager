const Dashboard = () => {
  return (
    <div style={{padding: '20px'}}>
      <h1 style={{color: 'white', fontSize: '30px'}}>Dashboard Test</h1>
      <p style={{color: '#ccc'}}>If you see this, the page works.</p>
      <div id="live-agents" style={{marginTop: '20px', color: '#aaa'}}>Loading agents...</div>
      <script dangerouslySetInnerHTML={{__html: `
        setTimeout(function() {
          fetch('/api/live/agents')
            .then(function(r) { return r.json() })
            .then(function(d) {
              document.getElementById('live-agents').innerText = 'Agents: ' + JSON.stringify(d)
            })
            .catch(function(e) {
              document.getElementById('live-agents').innerText = 'Error: ' + e
            })
        }, 1000)
      `}} />
    </div>
  )
}

export default Dashboard
