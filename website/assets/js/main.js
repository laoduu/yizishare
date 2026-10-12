/* YiziShare 官网交互脚本 */
(function () {
  'use strict';

  var $ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  /* ===== 导航滚动态 ===== */
  var nav = document.getElementById('nav');
  function onScroll() {
    if (nav) nav.classList.toggle('scrolled', window.scrollY > 8);
  }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  /* ===== 移动端菜单 ===== */
  var toggle = document.getElementById('navToggle');
  var links = document.getElementById('navLinks');
  if (toggle && links) {
    toggle.addEventListener('click', function () {
      var open = links.classList.toggle('open');
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    links.addEventListener('click', function (e) {
      if (e.target.tagName === 'A') {
        links.classList.remove('open');
        toggle.setAttribute('aria-expanded', 'false');
      }
    });
  }

  /* ===== 滚动出现 ===== */
  var reveals = $('.reveal');
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add('in');
          io.unobserve(entry.target);
        }
      });
    }, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });
    reveals.forEach(function (el, i) {
      el.style.transitionDelay = Math.min(i % 6, 5) * 55 + 'ms';
      io.observe(el);
    });
  } else {
    reveals.forEach(function (el) { el.classList.add('in'); });
  }

  /* ===== 应用模拟窗：聊天 / 共享 标签切换（还原 MainWindow 的 TabIndicator 行为）
     默认由 CSS 关键帧自动演示；用户一旦点击标签，加 .is-manual 停止演示、交还手动控制 ===== */
  var appEl = document.querySelector('.app');
  var appTabs = $('.app-tab');
  var appTabsBar = document.querySelector('.app-tabs');
  var appPanes = $('.app-pane');
  appTabs.forEach(function (tab) {
    tab.addEventListener('click', function () {
      var view = tab.getAttribute('data-view');
      if (appEl) appEl.classList.add('is-manual');
      appTabs.forEach(function (t) {
        var on = t === tab;
        t.classList.toggle('is-on', on);
        t.setAttribute('aria-selected', on ? 'true' : 'false');
      });
      appPanes.forEach(function (p) {
        p.classList.toggle('is-on', p.getAttribute('data-pane') === view);
      });
      if (appTabsBar) appTabsBar.setAttribute('data-active', view);
    });
  });

  /* ===== Hero 模拟窗：鼠标跟随的轻微 3D 转向 =====
     角度刻意压得很小（Y ≤ 4.5°、X ≤ 3.2°），保证图内 11px 小字不变形。
     方向取「窗口正面朝向光标」这一通行手感：光标在右 → 右边后退，光标在上 → 顶边后退。 */
  var mockWrap = document.querySelector('.hero-mock');
  var canHover = window.matchMedia('(hover: hover) and (pointer: fine)');
  var noMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  if (mockWrap && appEl) {
    var MAX_ROT_Y = 4.5, MAX_ROT_X = 3.2;
    mockWrap.addEventListener('mousemove', function (e) {
      // 触屏设备与系统「减弱动效」下直接不响应
      if (!canHover.matches || noMotion.matches) return;
      var r = mockWrap.getBoundingClientRect();
      if (!r.width || !r.height) return;
      var nx = (e.clientX - r.left) / r.width - 0.5;   // -0.5 → 0.5
      var ny = (e.clientY - r.top) / r.height - 0.5;
      mockWrap.classList.add('is-tilting');
      appEl.style.transform =
        'rotateY(' + (nx * MAX_ROT_Y * 2).toFixed(2) + 'deg) ' +
        'rotateX(' + (-ny * MAX_ROT_X * 2).toFixed(2) + 'deg)';
    });
    mockWrap.addEventListener('mouseleave', function () {
      mockWrap.classList.remove('is-tilting');
      appEl.style.transform = '';
    });
  }

  /* ===== 功能分类切换 ===== */
  var tabs = $('.tab');
  var cards = $('.card[data-group]');
  function applyGroup(group) {
    cards.forEach(function (card) {
      card.classList.toggle('hide', card.getAttribute('data-group') !== group);
    });
  }
  tabs.forEach(function (tab) {
    tab.addEventListener('click', function () {
      var group = tab.getAttribute('data-tab');
      tabs.forEach(function (t) {
        var on = t === tab;
        t.classList.toggle('is-on', on);
        t.setAttribute('aria-selected', on ? 'true' : 'false');
      });
      applyGroup(group);
    });
  });
  var onTab = tabs.filter(function (t) { return t.classList.contains('is-on'); })[0];
  if (onTab) applyGroup(onTab.getAttribute('data-tab'));

  /* ===== 更新日志切换 ===== */
  var logTabs = $('.log-tab');
  var logPanels = $('.log-panel');
  logTabs.forEach(function (tab) {
    tab.addEventListener('click', function () {
      var key = tab.getAttribute('data-log');
      logTabs.forEach(function (t) {
        var on = t === tab;
        t.classList.toggle('is-on', on);
        t.setAttribute('aria-selected', on ? 'true' : 'false');
      });
      logPanels.forEach(function (p) {
        p.classList.toggle('is-on', p.getAttribute('data-panel') === key);
      });
    });
  });

  /* ===== FAQ：同时只展开一项 ===== */
  var details = $('.faq details');
  details.forEach(function (d) {
    d.addEventListener('toggle', function () {
      if (!d.open) return;
      details.forEach(function (other) { if (other !== d) other.open = false; });
    });
  });
})();
