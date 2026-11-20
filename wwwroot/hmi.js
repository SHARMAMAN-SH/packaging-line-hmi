const element = (id) => document.getElementById(id);
const empty = '\u2014';
const history = [];
const canvas = element('trend');
const context = canvas.getContext('2d');

function drawTrend() {
  const width = Math.round(canvas.clientWidth * devicePixelRatio);
  const height = Math.round(canvas.clientHeight * devicePixelRatio);
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  context.clearRect(0, 0, width, height);
  context.strokeStyle = '#d4dfda';
  context.lineWidth = devicePixelRatio;
  for (const y of [1, height / 2, height - 1]) {
    context.beginPath();
    context.moveTo(0, y);
    context.lineTo(width, y);
    context.stroke();
  }

  context.strokeStyle = '#146b55';
  context.lineWidth = 2 * devicePixelRatio;
  context.beginPath();
  let previousValid = false;
  history.forEach((value, index) => {
    if (value !== null) {
      const x = (index + 60 - history.length) * (width - 2) / 59;
      const y = 2 + (height - 4) * (1 - Math.min(80, Math.max(0, value)) / 80);
      if (previousValid) context.lineTo(x, y);
      else context.moveTo(x, y);
    }
    previousValid = value !== null;
  });
  context.stroke();
}

function render(snapshot) {
  element('link').textContent = snapshot.connected ? 'OPC UA connected' : 'OPC UA offline';
  element('link').dataset.live = snapshot.connected;
  element('status').dataset.state = snapshot.state;
  element('state').textContent = snapshot.state;
  element('message').textContent = snapshot.message;

  const [conveyor, speed, count, jam] = snapshot.tags;
  element('conveyor').textContent = conveyor?.usable ? (conveyor.value ? 'Running' : 'Stopped') : 'Unavailable';
  element('discharge').textContent = jam?.usable ? (jam.value ? 'Jammed' : 'Clear') : 'Unavailable';
  element('speed').textContent = speed?.usable ? speed.value.toFixed(1) : empty;
  element('count').textContent = count?.usable ? count.value.toLocaleString() : empty;

  history.push(speed?.usable ? speed.value : null);
  if (history.length > 60) history.shift();
  drawTrend();

  const rows = snapshot.tags.map((tag) => {
    const row = document.createElement('tr');
    const label = document.createElement('th');
    label.scope = 'row';
    label.textContent = tag.tag;
    row.append(label);
    row.insertCell().textContent = tag.usable ? tag.value : empty;
    const status = row.insertCell();
    status.textContent = tag.status;
    status.dataset.usable = tag.usable;
    row.insertCell().textContent = tag.sourceTime ? new Date(tag.sourceTime).toISOString().slice(11, 19) : empty;
    return row;
  });
  element('tags').replaceChildren(...rows);
}

async function refresh() {
  try {
    const response = await fetch('/api/line', { signal: AbortSignal.timeout(2500) });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    render(await response.json());
  } catch {
    render({ connected: false, state: 'Offline', message: 'HMI service unavailable.', tags: [] });
  } finally {
    setTimeout(refresh, 1000);
  }
}

window.addEventListener('resize', drawTrend);
refresh();
