/* ==========================================================================
   motion.js — 动效层（表现层，零业务耦合）
   提供三件事：
     1. [data-reveal] 元素滚动进入视口时淡入上浮（容器标 data-stagger 则依次错开）
     2. [data-countup] 元素进入视口时从 0 滚动到目标值
     3. window.gbCountUp 供页面脚本主动控制（如分析页元 / 万元切换时重放）
   约定：绝不改写业务数据，只改元素文本与样式类；脚本失效时页面内容仍完整可见。
   ========================================================================== */
(() => {
  'use strict';

  const doc = document.documentElement;
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const hasIO = typeof IntersectionObserver === 'function';

  // 供 <head> 里的兜底计时器判断脚本是否真的跑起来了
  window.__gbMotion = true;

  /* ------------------------------ 数字滚动 ------------------------------ */

  const nf = (digits) => new Intl.NumberFormat('zh-CN', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits
  });

  const running = new Set();

  function stop(el) {
    if (!el) return;
    const handle = el.__gbRaf;
    if (handle) {
      cancelAnimationFrame(handle);
      el.__gbRaf = 0;
    }
    running.delete(el);
  }

  function stopAll() {
    Array.from(running).forEach(stop);
  }

  /**
   * 把元素文本从 0 滚到 value。
   * @param {HTMLElement} el 目标元素
   * @param {number} value 目标数值
   * @param {{digits?:number, duration?:number, delay?:number, format?:(n:number)=>string}} opts
   */
  function play(el, value, opts) {
    if (!el || !Number.isFinite(value)) return;
    stop(el);
    const o = opts || {};
    const digits = o.digits == null ? 0 : o.digits;
    const format = typeof o.format === 'function' ? o.format : (n) => nf(digits).format(n);

    if (reduced || value === 0) {
      el.textContent = format(value);
      return;
    }

    const duration = o.duration == null ? 900 : o.duration;
    const startAt = performance.now() + (o.delay == null ? 0 : o.delay);

    const step = (now) => {
      const t = Math.min(1, Math.max(0, (now - startAt) / duration));
      const eased = 1 - Math.pow(1 - t, 3);
      el.textContent = format(value * eased);
      if (t < 1) {
        el.__gbRaf = requestAnimationFrame(step);
      } else {
        el.textContent = format(value);
        el.__gbRaf = 0;
        running.delete(el);
      }
    };

    running.add(el);
    el.__gbRaf = requestAnimationFrame(step);
  }

  window.gbCountUp = { play, stop, stopAll };

  /* ------------------------------ 入场动效 ------------------------------ */

  const revealTargets = Array.from(document.querySelectorAll('[data-reveal]'));

  // 容器上的 data-stagger：给子项依次编号，形成瀑布感
  document.querySelectorAll('[data-stagger]').forEach((box) => {
    const items = Array.from(box.querySelectorAll(':scope > [data-reveal]'));
    items.forEach((el, i) => el.style.setProperty('--reveal-i', String(i)));
  });

  function reveal(el) {
    el.classList.add('is-in');
  }

  if (!revealTargets.length) {
    // 没有可入场元素时不做任何处理
  } else if (reduced || !hasIO) {
    revealTargets.forEach(reveal);
  } else {
    const io = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        reveal(entry.target);
        io.unobserve(entry.target);
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -6% 0px' });

    revealTargets.forEach((el) => io.observe(el));

    // 兜底：某些元素永远不进视口（被折叠 / 高度为 0）时不至于一直隐身
    window.setTimeout(() => {
      document.querySelectorAll('[data-reveal]:not(.is-in)').forEach(reveal);
    }, 6000);
  }

  /* --------------------------- 声明式数字滚动 --------------------------- */

  const countupEls = Array.from(document.querySelectorAll('[data-countup]'));

  function playDeclared(el, index) {
    const value = Number(el.dataset.countup);
    if (!Number.isFinite(value)) return;
    play(el, value, {
      digits: Number(el.dataset.countupDigits || 0),
      duration: Number(el.dataset.countupDuration || 900),
      delay: Number(el.dataset.countupDelay || 0) + index * 70
    });
  }

  if (countupEls.length) {
    if (reduced || !hasIO) {
      countupEls.forEach((el, i) => playDeclared(el, 0));
    } else {
      const cu = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          playDeclared(entry.target, Number(entry.target.dataset.countupOrder || 0));
          cu.unobserve(entry.target);
        });
      }, { threshold: 0.4 });

      countupEls.forEach((el, i) => {
        el.dataset.countupOrder = String(i);
        cu.observe(el);
      });
    }
  }

  /* ------------------------------ 就绪标记 ------------------------------ */

  doc.classList.add('motion-on');
})();
