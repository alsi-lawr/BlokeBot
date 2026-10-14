export class CornerDragMotion {
  constructor(x, y, reduced) {
    this.x = x;
    this.y = y;
    this.last = performance.now();
    this.vx = 0;
    this.vy = 0;
    this.reduced = reduced;
    this.angle = { position: 0, velocity: 0 };
    this.pitch = { position: 0, velocity: 0 };
    this.roll = { position: 0, velocity: 0 };
    this.lift = { position: 0, velocity: 0 };
  }

  advance(s, target, stiffness, damping, dt) {
    const steps = Math.ceil(dt / .008), step = dt / steps;
    for (let i = 0; i < steps; i++) {
      s.velocity += ((target - s.position) * stiffness - s.velocity * damping) * step;
      s.position += s.velocity * step;
    }
  }

  paint(card, now, x, y) {
    const dt = Math.min(.032, Math.max(.001, (now - this.last) / 1000));
    const blend = 1 - Math.exp(-dt * 42);
    this.vx += ((x - this.x) / dt - this.vx) * blend;
    this.vy += ((y - this.y) / dt - this.vy) * blend;
    this.x = x;
    this.y = y;
    this.last = now;
    if (this.reduced) {
      card.style.transform = 'none';
      return;
    }
    const limit = (value, bound) => Math.max(-bound, Math.min(bound, value));
    const horizontalSwing = limit(this.vx * .0024, 2);
    const verticalSwing = limit(this.vy * .006, 5);
    this.advance(this.angle, limit(horizontalSwing + verticalSwing, 5), 1600, 80, dt);
    this.advance(this.pitch, limit(this.vy * .0027, 2), 1200, 70, dt);
    this.advance(this.roll, limit(-this.vx * .0009, 1), 1800, 86, dt);
    this.advance(this.lift, 1, 900, 60, dt);
    card.style.transform = this.transform();
  }

  transform(amount = 1) {
    return `perspective(800px) rotateZ(${this.angle.position * amount}deg) rotateX(${this.pitch.position * amount}deg) rotateY(${this.roll.position * amount}deg) scale(${1 + this.lift.position * .02 * amount})`;
  }
}

export function createDragPreview(grid) {
  const layer = document.createElement('div'), target = document.createElement('div');
  layer.className = 'dashboard-drag-preview';
  layer.setAttribute('popover', 'manual');
  layer.setAttribute('aria-hidden', 'true');
  layer.inert = true;
  target.className = 'dashboard-drop-target';
  target.setAttribute('aria-hidden', 'true');
  target.hidden = true;
  document.body.append(layer);
  grid.append(target);
  let current = null, settling = null;

  function clear() {
    const old = current, landing = settling;
    current = null;
    settling = null;
    landing?.animations.forEach(a => a.cancel());
    old?.card.classList.remove('dashboard-drag-source');
    landing?.destination.classList.remove('dashboard-drag-source');
    if (layer.matches(':popover-open')) layer.hidePopover();
    layer.replaceChildren();
    target.hidden = true;
  }

  return {
    start(g) {
      clear();
      current = g;
      const card = g.card.cloneNode(true);
      for (const node of [card, ...card.querySelectorAll('*')]) {
        for (const name of ['id', 'for', 'aria-labelledby', 'aria-describedby', 'data-widget-id', 'data-grid-move', 'data-grid-resize']) {
          node.removeAttribute(name);
        }
      }
      card.style.gridColumn = '';
      card.style.gridRow = '';
      card.style.order = '';
      card.style.transformOrigin = `${g.grabX}px ${g.grabY}px`;
      g.card.classList.add('dashboard-drag-source');
      layer.replaceChildren(card);
      layer.showPopover();
      layer.style.width = `${g.rect.width}px`;
      layer.style.height = `${g.rect.height}px`;
    },
    paint(g, now, cell, gap, valid) {
      if (current !== g) return;
      layer.style.transform = `translate3d(${g.left}px,${g.top}px,0)`;
      const card = layer.firstElementChild;
      if (!g.resize) g.motion.paint(card, now, g.x, g.y);
      else {
        layer.style.width = `${g.target.width * cell.x - gap.x}px`;
        layer.style.height = `${g.target.height * cell.y - gap.y}px`;
      }
      target.hidden = false;
      target.dataset.valid = String(valid);
      Object.assign(target.style, {
        left: `${g.target.column * cell.x}px`, top: `${g.target.row * cell.y}px`,
        width: `${g.target.width * cell.x - gap.x}px`, height: `${g.target.height * cell.y - gap.y}px`,
      });
    },
    land(g, destination) {
      if (current !== g) return;
      target.hidden = true;
      if (g.motion.reduced || g.resize || !destination) {
        clear();
        return;
      }
      const r = destination.getBoundingClientRect(), card = layer.firstElementChild;
      const dx = r.left - g.left, dy = r.top - g.top;
      destination.classList.add('dashboard-drag-source');
      const place = (x, y) => `translate3d(${x}px,${y}px,0)`;
      const glide = layer.animate([
        { transform: place(g.left, g.top), offset: 0, easing: 'cubic-bezier(.18,.7,.3,1)' },
        { transform: place(r.left - dx * .04, r.top - dy * .04), offset: .72, easing: 'ease-out' },
        { transform: place(r.left - dx * .004, r.top - dy * .004), offset: .9, easing: 'ease-out' },
        { transform: place(r.left, r.top), offset: 1 },
      ], { duration: 180, fill: 'forwards' });
      const land = card.animate([
        { transform: card.style.transform, boxShadow: getComputedStyle(card).boxShadow, offset: 0, easing: 'cubic-bezier(.18,.7,.3,1)' },
        { transform: g.motion.transform(.15), offset: .65, easing: 'ease-out' },
        { transform: g.motion.transform(.018), offset: .86, easing: 'ease-out' },
        { transform: 'none', boxShadow: getComputedStyle(destination).boxShadow, offset: 1 },
      ], { duration: 200, fill: 'forwards' });
      const pending = { destination, animations: [glide, land] };
      settling = pending;
      Promise.all(pending.animations.map(a => a.finished)).then(() => {
        if (current === g && settling === pending) clear();
      }, () => {});
    },
    clear,
    dispose() {
      clear();
      layer.remove();
      target.remove();
    },
  };
}
