'use strict';

let graphData = null;
let resizeTimer = null;

window.addEventListener('DOMContentLoaded', async () => {
  const params = new URLSearchParams(window.location.search);
  const root = params.get('root');
  if (!root) {
    showError('No root schema specified.');
    return;
  }

  try {
    const res = await fetch(`./data/${encodeURIComponent(root)}.graph.json`);
    if (!res.ok) throw new Error(`Failed to load graph data (${res.status}).`);
    graphData = await res.json();
    renderGraph(graphData);
  } catch (e) {
    showError(e.message);
  }
});

window.addEventListener('resize', () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    if (graphData) renderGraph(graphData);
  }, 120);
});

function showError(msg) {
  const wrapper = document.getElementById('wrapper');
  wrapper.innerHTML = `<div class="error-msg">${escapeHtml(msg)}</div>`;
}

function renderGraph(dto) {
  const outer = document.getElementById('outer');
  const wrapper = document.getElementById('wrapper');
  wrapper.innerHTML = '';

  let legend = document.getElementById('legend');
  if (!legend) {
    legend = document.createElement('div');
    legend.id = 'legend';
    outer.insertBefore(legend, wrapper);
  }
  legend.innerHTML = buildLegendHtml();

  const byLevel = new Map();
  for (const node of dto.nodes) {
    if (!byLevel.has(node.level)) byLevel.set(node.level, []);
    byLevel.get(node.level).push(node);
  }

  const levels = [...byLevel.keys()].sort((a, b) => a - b);
  for (const level of levels) {
    const col = document.createElement('div');
    col.className = 'level-col';
    col.dataset.level = level;
    for (const node of byLevel.get(level)) {
      col.appendChild(createCard(node));
    }
    wrapper.appendChild(col);
  }

  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.id = 'arrows';
  wrapper.appendChild(svg);

  setTimeout(() => {
    svg.setAttribute('width', wrapper.scrollWidth);
    svg.setAttribute('height', wrapper.scrollHeight);
    drawArrows(dto.edges, wrapper, svg);
  }, 0);
}

function buildLegendHtml() {
  return `
    <div class="legend-item">
      <svg width="44" height="14" viewBox="0 0 44 14">
        <line x1="2" y1="7" x2="33" y2="7" stroke="#5B8EDE" stroke-width="2" opacity="0.85"/>
        <polygon points="33,3.5 43,7 33,10.5" fill="#5B8EDE" opacity="0.85"/>
      </svg>
      <span>A → B: 순방향 참조</span>
    </div>
    <div class="legend-item">
      <svg width="44" height="14" viewBox="0 0 44 14">
        <line x1="11" y1="7" x2="42" y2="7" stroke="#E05252" stroke-width="2" opacity="0.85"/>
        <polygon points="11,3.5 1,7 11,10.5" fill="#E05252" opacity="0.85"/>
      </svg>
      <span>A ← B: 역방향 참조</span>
    </div>
    <div class="legend-item">
      <svg width="44" height="14" viewBox="0 0 44 14">
        <path d="M 4 7 C 12 1, 32 1, 40 7 C 32 13, 12 13, 4 7" fill="none" stroke="#888" stroke-width="2" opacity="0.85"/>
      </svg>
      <span>자기 참조</span>
    </div>`;
}

function createCard(node) {
  const card = document.createElement('div');
  card.className = 'card';
  card.dataset.schema = node.schema;
  card.dataset.level = node.level;

  const title = document.createElement('div');
  title.className = 'card-title';
  title.textContent = node.schema;
  card.appendChild(title);

  const table = document.createElement('table');
  table.className = 'schema-table';
  const tbody = document.createElement('tbody');

  for (const col of node.columns) {
    const tr = document.createElement('tr');
    if (col.ref) tr.classList.add('ref-row');
    tr.dataset.colPath = col.path;

    const td = document.createElement('td');
    td.className = 'td-col';
    td.textContent = col.path;
    if (col.ref) {
      const badge = document.createElement('span');
      badge.className = 'ref-badge';
      badge.textContent = ` → ${col.ref}`;
      td.appendChild(badge);
    }
    tr.appendChild(td);
    tbody.appendChild(tr);
  }

  table.appendChild(tbody);
  card.appendChild(table);
  return card;
}

function drawArrows(edges, wrapper, svg) {
  const wrapperRect = wrapper.getBoundingClientRect();
  const LANE = 14;
  const FWD_SHIFT = 18;
  const BACK_SHIFT = -18;

  // First pass: assign each arrow a lane index within its gap group.
  // Gap key separates forward vs backward per level pair so they use different midX bands.
  const laneCounters = new Map();
  const edgeInfos = [];

  for (const edge of edges) {
    const fromCard = wrapper.querySelector(`.card[data-schema="${CSS.escape(edge.fromNode)}"]`);
    const toCard = wrapper.querySelector(`.card[data-schema="${CSS.escape(edge.toNode)}"]`);
    if (!fromCard || !toCard) continue;

    const fromRow = fromCard.querySelector(`tr[data-col-path="${CSS.escape(edge.fromColumn)}"]`);
    const toRow = toCard.querySelector(`tr[data-col-path="${CSS.escape(edge.toColumn)}"]`);
    if (!fromRow || !toRow) continue;

    const isSelf = edge.fromNode === edge.toNode;
    const fromLevel = parseInt(fromCard.dataset.level ?? '0');
    const toLevel = parseInt(toCard.dataset.level ?? '0');
    const isBackward = isSelf || toLevel < fromLevel;

    const lo = Math.min(fromLevel, toLevel);
    const hi = Math.max(fromLevel, toLevel);
    const gapKey = isSelf
      ? `self:${edge.fromNode}`
      : `${isBackward ? 'back' : 'fwd'}:${lo}:${hi}`;

    const laneIdx = laneCounters.get(gapKey) ?? 0;
    laneCounters.set(gapKey, laneIdx + 1);
    edgeInfos.push({ edge, fromCard, toCard, fromRow, toRow, isSelf, isBackward, laneIdx, gapKey });
  }

  // Second pass: draw with lane-offset midX so vertical segments don't overlap.
  for (const { edge, fromCard, toCard, fromRow, toRow, isSelf, isBackward, laneIdx, gapKey } of edgeInfos) {
    const fromCardRect = cellOffset(fromCard, wrapperRect, wrapper);
    const fromRowRect = cellOffset(fromRow, wrapperRect, wrapper);
    const toCardRect = cellOffset(toCard, wrapperRect, wrapper);
    const toRowRect = cellOffset(toRow, wrapperRect, wrapper);

    const y1 = fromRowRect.top + fromRowRect.height / 2;
    const y2 = toRowRect.top + toRowRect.height / 2;
    const total = laneCounters.get(gapKey);
    const laneOffset = (laneIdx - (total - 1) / 2) * LANE;
    let d;

    if (isSelf) {
      const x1 = fromCardRect.right;
      const R = 50 + laneIdx * LANE;
      d = `M ${x1} ${y1} C ${x1 + R} ${y1}, ${x1 + R} ${y2}, ${x1} ${y2}`;
    } else if (isBackward) {
      const x1 = fromCardRect.left;
      const x2 = toCardRect.right;
      const midX = (x1 + x2) / 2 + BACK_SHIFT + laneOffset;
      d = `M ${x1} ${y1} C ${midX} ${y1}, ${midX} ${y2}, ${x2} ${y2}`;
    } else {
      const x1 = fromCardRect.right;
      const x2 = toCardRect.left;
      const midX = (x1 + x2) / 2 + FWD_SHIFT + laneOffset;
      d = `M ${x1} ${y1} C ${midX} ${y1}, ${midX} ${y2}, ${x2} ${y2}`;
    }

    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', d);
    path.setAttribute('class', isSelf ? 'arrow-path-self' : isBackward ? 'arrow-path-back' : 'arrow-path');
    svg.appendChild(path);
  }
}

function cellOffset(el, wrapperRect, wrapper) {
  const r = el.getBoundingClientRect();
  const sl = wrapper.scrollLeft;
  const st = wrapper.scrollTop;
  return {
    left: r.left - wrapperRect.left + sl,
    top: r.top - wrapperRect.top + st,
    right: r.right - wrapperRect.left + sl,
    bottom: r.bottom - wrapperRect.top + st,
    width: r.width,
    height: r.height
  };
}

function escapeHtml(str) {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
