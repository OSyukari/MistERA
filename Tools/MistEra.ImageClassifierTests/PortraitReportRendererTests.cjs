'use strict';

// Usage: node Tools/MistEra.ImageClassifierTests/PortraitReportRendererTests.cjs [report.html]
// Synthetic fixtures always run; an optional generated report also exercises its embedded data.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

assert.ok(process.argv.length <= 3, 'Expected at most one optional HTML path');
const htmlPath = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.resolve(__dirname, '../../Assets/Editor/ImageClassifier/DatabaseReport_Template.html');
const html = fs.readFileSync(htmlPath, 'utf8');
const scripts = [...html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)];
const renderers = scripts.filter(match => /function\s+renderHandlers\s*\(/.test(match[1]));
assert.equal(renderers.length, 1, 'Expected exactly one portrait renderer script');
const renderer = new vm.Script(renderers[0][1], { filename: htmlPath });
let actualReport;
if (process.argv[2]) {
  const data = scripts.map(match => match[1].match(/window\.REPORT\s*=\s*([\s\S]*);\s*$/))
    .filter(Boolean);
  assert.equal(data.length, 1, 'Expected exactly one embedded REPORT assignment');
  actualReport = JSON.parse(data[0][1].replace('/*__REPORT_DATA__*/', ''));
}
const middleDot = '\u00b7';

function fixture() {
  return {
    title: 'Portrait renderer regression fixture',
    generated: 'test',
    rules: 'test',
    lint: [],
    // Deliberately reverse folder and file order to exercise renderer sorting.
    folders: ['Zulu', 'alpha'].map(name => ({
      name,
      base: `./${name}/`,
      items: ['b.png', 'a.png'].map(file => ({
        path: file, category: 'cat', cls: 'cls', status: 'confident'
      }))
    })),
    portrait: {
      handlers: ['H0', 'H1'].map(name => ({ folders: [name], self: [], target: [] })),
      variants: [
        { h: 0, pool: 0, tags: ['FIRST'], excl: [], classifications: ['cls', 'missing', '<token>&"'] },
        { h: 0, pool: 0, tags: ['SECOND'], excl: [], classifications: ['cls', 'second_missing'] },
        { h: 0, pool: 2, tags: ['EMPTY'], excl: [], classifications: ['empty_missing'] },
        { h: 1, pool: 3, tags: ['LAST'], excl: [], classifications: ['cls'] }
      ],
      roots: [
        { slots: [
          { v: 0, items: [0, 1], shadow: -1 },
          { v: 1, items: [0, 1], shadow: 0 },
          { v: 2, items: [], shadow: -1 },
          { v: 3, items: [0], shadow: -1 }
        ] },
        { slots: [
          { v: 0, items: [2, 3], shadow: -1 },
          { v: 1, items: [2, 3], shadow: -1 },
          { v: 2, items: [], shadow: -1 },
          { v: 3, items: [], shadow: -1 }
        ] }
      ],
      unplaced: []
    }
  };
}

function element() {
  return {
    value: '', checked: false, open: false, innerHTML: '', textContent: '',
    style: { setProperty() {} },
    listeners: {},
    appendChild() {},
    addEventListener(type, listener) { this.listeners[type] = listener; },
    querySelector() { return element(); }
  };
}

function render({ empty = false, file = '', report = fixture() } = {}) {
  const elements = new Map();
  function get(id) {
    if (!elements.has(id)) elements.set(id, element());
    return elements.get(id);
  }
  get('f-file').value = file;
  const storage = new Map([
    ['imgreport.view', 'handler'], ['imgreport.empty', empty ? '1' : '0']
  ]);
  renderer.runInNewContext({
    window: { REPORT: report, scrollTo() {}, addEventListener() {} },
    location: { pathname: htmlPath.replace(/\\/g, '/') },
    document: {
      getElementById: get, createElement: element,
      documentElement: element(), addEventListener() {}
    },
    localStorage: {
      getItem: key => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, String(value))
    },
    setTimeout, clearTimeout
  }, { timeout: 1000 });

  function snapshot() {
    const output = get('cats').innerHTML;
    // Parse only the renderer's stable row/anchor markup, not arbitrary HTML.
    const parseRows = markup => [...markup.matchAll(/<div class="row(?: empty-variant)?"><div class="name"><b>(.*?)<\/b>([\s\S]*?)<div class="thumbs">([\s\S]*?)<\/div><\/div>/g)]
      .map(match => ({
        label: match[1], meta: match[2], thumbs: match[3],
        classifications: match[2].match(/<small>All classifications used: (.*?)<\/small>/)?.[1],
        placeholders: [...match[3].matchAll(/<span\b([^>]*\bclass="repeated-images"[^>]*)>\+(\d+)<\/span>/g)]
          .map(placeholder => ({ html: placeholder[0], count: Number(placeholder[2]) })),
        empty: match[0].startsWith('<div class="row empty-variant">'),
        links: [...match[3].matchAll(/<a\b([^>]*)>/g)].map(link => {
          const attribute = name => link[1].match(new RegExp(`\\b${name}="([^"]*)"`))?.[1];
          return {
            href: attribute('href'), title: attribute('title'),
            shadowed: attribute('class').split(/\s+/).includes('shadowed'),
            index: Number(attribute('data-i'))
          };
        })
      }));
    const sections = [...output.matchAll(/<section class="cat">([\s\S]*?)<\/section>/g)]
      .map(match => ({ html: match[1], rows: parseRows(match[1]) }));
    return { html: output, rows: parseRows(output), sections, count: get('count').textContent };
  }

  return {
    snapshot,
    async input(id, value) {
      get(id).value = value;
      assert.equal(typeof get(id).listeners.input, 'function', `${id} input listener`);
      get(id).listeners.input();
      await new Promise(resolve => setTimeout(resolve, 180));
      return snapshot();
    },
    change(id, value) {
      const target = get(id);
      if (typeof value === 'boolean') target.checked = value;
      else target.value = value;
      assert.equal(typeof target.listeners.change, 'function', `${id} change listener`);
      target.listeners.change();
      return snapshot();
    }
  };
}

test('same-pool variants retain rows and membership but draw images only once globally', () => {
  const { rows, sections } = render().snapshot();
  assert.deepEqual(rows.map(row => row.label), ['FIRST', 'SECOND', 'LAST']);
  assert.deepEqual(rows.map(row => row.links.length), [4, 0, 0]);
  assert.deepEqual(rows.map(row => row.placeholders.map(box => box.count)), [[], [4], [1]]);
  rows.forEach((row, index) => {
    assert.ok(!row.empty, row.label);
    assert.ok(row.meta.includes(`<small>${[4, 4, 1][index]} images</small>`), row.label);
  });
  assert.ok(sections[0].html.includes('<span class="n">(8)</span>'));
  assert.ok(sections[1].html.includes('<span class="n">(1)</span>'));
});

test('shadow state and shadowing label remain specific to each variant and folder', () => {
  const report = fixture();
  report.portrait.roots.forEach(root => { root.slots[0].items = []; });
  const { rows } = render({ report }).snapshot();
  assert.equal(rows.find(row => row.label === 'SECOND').links.length, 4);
  assert.ok(rows.find(row => row.label === 'SECOND').meta.includes('2 shadowed in their folder'));
  for (const [label, folder, shadowing] of [['SECOND', 'Zulu', 'FIRST']]) {
    const row = rows.find(candidate => candidate.label === label);
    assert.ok(row, `${label} row exists`);
    assert.equal(row.links.filter(link => link.shadowed).length, folder ? 2 : 0);
    for (const link of row.links) {
      const shadowed = link.href.startsWith(`./${folder}/`);
      assert.equal(link.shadowed, shadowed, `${label}: ${link.href}`);
      assert.equal(link.title, shadowed ? `shadowed in its folder by ${shadowing}` : undefined);
    }
  }
});

test('handler and variant priority order includes empty rows with local variant numbers', () => {
  const { html: output, rows } = render({ empty: true }).snapshot();
  assert.deepEqual(rows.map(row => row.label), ['FIRST', 'SECOND', 'EMPTY', 'LAST']);
  assert.ok(output.indexOf('<b>H0</b>') < output.indexOf('<b>H1</b>'));
  rows.forEach((row, index) => {
    assert.ok(row.meta.includes(`<small>Variant ${[1, 2, 3, 1][index]}</small>`), row.label);
  });
});

test('empty rows toggle through the actual change listener', () => {
  const harness = render();
  assert.ok(!harness.snapshot().rows.some(row => row.label === 'EMPTY'));
  const shown = harness.change('f-empty', true);
  const row = shown.rows.find(candidate => candidate.label === 'EMPTY');
  assert.ok(row.empty);
  assert.equal(row.links.length, 0);
  assert.ok(row.meta.includes('0 images in any folder'));
  assert.ok(!harness.change('f-empty', false).rows.some(candidate => candidate.label === 'EMPTY'));
});

test('images sort by case-insensitive folder then path and receive contiguous thumbnail indices', () => {
  const { rows } = render().snapshot();
  assert.deepEqual(rows[0].links.map(link => link.href), [
    './alpha/a.png', './alpha/b.png', './Zulu/a.png', './Zulu/b.png'
  ]);
  assert.deepEqual(rows.flatMap(row => row.links.map(link => link.index)), [0, 1, 2, 3]);
});

test('thumbnail and distinct counts both exclude repeated images', () => {
  assert.equal(render().snapshot().count, `4 thumbnails ${middleDot} 4 of 4 images`);
});

test('partial filtering counts matching images per variant and retains filtered-empty rows', () => {
  const { rows, count } = render({ empty: true, file: 'a.png' }).snapshot();
  assert.deepEqual(rows.map(row => row.label), ['FIRST', 'SECOND', 'EMPTY', 'LAST']);
  assert.deepEqual(rows.map(row => row.links.length), [2, 0, 0, 0]);
  assert.deepEqual(rows.map(row => row.placeholders.map(box => box.count)), [[], [2], [], []]);
  assert.ok(!rows[1].empty);
  assert.ok(rows[1].meta.includes('<small>2 images</small>'));
  assert.ok(rows[2].meta.includes('0 images in any folder'));
  assert.ok(rows[3].meta.includes('0 of 1 images pass the filters'));
  assert.ok(rows[3].meta.includes('<small>Variant 1</small>'));
  assert.equal(count, `2 thumbnails ${middleDot} 2 of 4 images`);
});

test('fully filtered variants retain independent totals when empty rows are enabled', () => {
  const { rows, count } = render({ empty: true, file: 'NO_MATCH' }).snapshot();
  assert.deepEqual(rows.map(row => row.label), ['FIRST', 'SECOND', 'EMPTY', 'LAST']);
  rows.forEach(row => { assert.ok(row.empty); assert.equal(row.links.length, 0); });
  assert.ok(rows[0].meta.includes('0 of 4 images pass the filters'));
  assert.ok(rows[1].meta.includes('0 of 4 images pass the filters'));
  assert.ok(rows[2].meta.includes('0 images in any folder'));
  assert.ok(rows[3].meta.includes('0 of 1 images pass the filters'));
  assert.equal(count, `0 thumbnails ${middleDot} 0 of 4 images`);
});

test('fully filtered variants disappear when empty rows are disabled', () => {
  const { html: output, rows, count } = render({ file: 'NO_MATCH' }).snapshot();
  assert.equal(rows.length, 0);
  assert.ok(output.includes('Nothing matches the filters.'));
  assert.equal(count, `0 thumbnails ${middleDot} 0 of 4 images`);
});

test('sad draws one unique image and counts two images shown by earlier conditions', () => {
  const report = fixture();
  report.portrait.variants = ['disappointed', 'third', 'sad'].map(tags => ({
    h: 0, tags: [tags], excl: [], classifications: [tags]
  }));
  report.portrait.roots = [{ slots: [
    { v: 0, items: [0], shadow: -1 },
    { v: 1, items: [1], shadow: -1 },
    { v: 2, items: [0, 1, 2], shadow: -1 }
  ] }];
  const { rows, count } = render({ report }).snapshot();
  assert.deepEqual(rows.map(row => row.label), ['disappointed', 'third', 'sad']);
  assert.deepEqual(rows[2].links.map(link => link.href), ['./alpha/b.png']);
  assert.deepEqual(rows[2].placeholders.map(box => box.count), [2]);
  assert.ok(rows[2].meta.includes('<small>3 images</small>'));
  assert.equal(count, `3 thumbnails ${middleDot} 3 of 4 images`);
});

test('triple reuse counts repeated images rather than their previous uses', () => {
  const report = fixture();
  report.portrait.roots.forEach((root, index) => {
    root.slots[3].items = index ? [2, 3] : [0, 1];
  });
  const { rows } = render({ report }).snapshot();
  assert.deepEqual(rows.map(row => row.links.length), [4, 0, 0]);
  assert.deepEqual(rows.map(row => row.placeholders.map(box => box.count)), [[], [4], [4]]);
});

test('deduplication resets after filter input rerenders and view switches', async () => {
  const harness = render();
  const original = harness.snapshot();
  const filtered = await harness.input('f-file', 'a.png');
  assert.deepEqual(filtered.rows.map(row => row.links.length), [2, 0]);
  assert.deepEqual(filtered.rows[1].placeholders.map(box => box.count), [2]);
  assert.deepEqual(await harness.input('f-file', ''), original);
  const classes = harness.change('f-view', 'class');
  assert.equal(classes.count, '4 of 4 images');
  assert.deepEqual(harness.change('f-view', 'handler'), original);
});

test('identical basenames in different folders remain distinct images', () => {
  const { rows } = render({ file: 'a.png' }).snapshot();
  assert.deepEqual(rows[0].links.map(link => link.href), ['./alpha/a.png', './Zulu/a.png']);
  assert.deepEqual(rows[1].placeholders.map(box => box.count), [2]);
});

test('configured classifications include missing images and survive filters and empty rows', async () => {
  const harness = render({ empty: true });
  const expected = ['cls, missing, &lt;token&gt;&amp;&quot;', 'cls, second_missing', 'empty_missing', 'cls'];
  assert.deepEqual(harness.snapshot().rows.map(row => row.classifications), expected);
  assert.deepEqual((await harness.input('f-file', 'NO_MATCH')).rows.map(row => row.classifications), expected);
  await harness.input('f-file', '');
  assert.deepEqual((await harness.input('f-class', 'cls')).rows.map(row => row.classifications), expected);
});

test('explicitly empty exported classifications do not fall back to image classes', () => {
  const report = fixture();
  report.portrait.variants[0].classifications = [];
  const { rows } = render({ report }).snapshot();
  assert.equal(rows[0].links.length, 4);
  assert.equal(rows[0].classifications, '(none)');
});

test('legacy classification fallback uses sorted unfiltered memberships, not visible thumbnails', () => {
  const report = fixture();
  report.portrait.variants.forEach(variant => { delete variant.classifications; });
  report.folders[0].items[0].cls = 'zeta';
  report.folders[1].items[0].cls = 'alpha';
  const { rows } = render({ empty: true, file: 'a.png', report }).snapshot();
  assert.deepEqual(rows.map(row => row.classifications), ['alpha, cls, zeta', 'alpha, cls, zeta', '(none)', 'zeta']);
  assert.equal(rows[1].links.length, 0);
  assert.ok(rows[3].empty);
});

test('metadata escapes classification tokens and variant and handler labels', () => {
  const report = fixture();
  const token = '<token>&"';
  const escaped = '&lt;token&gt;&amp;&quot;';
  report.portrait.variants[0].tags = [token];
  report.portrait.variants[0].excl = [token];
  report.portrait.handlers[0] = { folders: [token], self: [token], target: [token] };
  const { html: output, rows } = render({ report }).snapshot();
  assert.equal(rows[0].label, `${escaped} ${middleDot} not ${escaped}`);
  assert.ok(rows[0].classifications.includes(escaped));
  assert.ok(output.includes(`<b>${escaped}</b> ${middleDot} self ${escaped} ${middleDot} target ${escaped}`));
  assert.ok(!output.includes(token));
});

test('repeated-image placeholders are single non-selectable spans without thumbnail indices or anchors', () => {
  const { rows, html: output } = render().snapshot();
  rows.slice(1).forEach(row => {
    assert.equal(row.placeholders.length, 1);
    assert.equal(row.thumbs, row.placeholders[0].html);
    assert.ok(!/data-i\s*=|<a\b|<img\b/.test(row.thumbs));
    const n = row.placeholders[0].count;
    assert.ok(row.thumbs.includes(`title="${n} images already shown above"`));
    assert.ok(row.thumbs.includes(`aria-label="${n} images already shown above"`));
  });
  assert.equal([...output.matchAll(/\bdata-i="/g)].length, 4);
});

test('ordinary classification view ignores variant reuse and draws each classified item', () => {
  const harness = render();
  const { rows, count } = harness.change('f-view', 'class');
  assert.deepEqual(rows.map(row => row.label), ['cls']);
  assert.equal(rows[0].links.length, 4);
  assert.equal(rows[0].placeholders.length, 0);
  assert.equal(rows[0].classifications, undefined);
  assert.deepEqual(rows[0].links.map(link => link.index), [0, 1, 2, 3]);
  assert.equal(count, '4 of 4 images');
});

test('ordinary classification view does not apply handler URL deduplication', () => {
  const report = fixture();
  report.folders[1].base = report.folders[0].base;
  const harness = render({ report });
  assert.equal(harness.snapshot().rows.flatMap(row => row.links).length, 2);
  const { rows, count } = harness.change('f-view', 'class');
  const hrefs = rows.flatMap(row => row.links.map(link => link.href));
  assert.equal(hrefs.length, 4);
  assert.equal(new Set(hrefs).size, 2);
  assert.ok(rows.every(row => row.placeholders.length === 0));
  assert.equal(count, '4 of 4 images');
});

if (actualReport) test('actual report simplifies group selectors and preserves broad pool memberships', () => {
  const portrait = actualReport.portrait;
  assert.ok(portrait && !portrait.error, 'Actual report has portrait usage data');
  const retired = new Set([
    'group_sex_multiple', 'group_sex_mmfthreesome', 'triple_penetration', 'multiple_penetration'
  ]);
  portrait.variants.forEach((variant, index) => {
    assert.ok(variant.tags.every(tag => !retired.has(tag)),
      `Variant ${index}: no retired group selector tags`);
  });
  const groups = portrait.handlers.map((handler, index) => ({
    handler, index,
    variants: portrait.variants.map((variant, v) => ({ variant, v }))
      .filter(({ variant }) => variant.h === index && variant.tags.includes('group_sex'))
  })).filter(group => group.variants.length);
  assert.ok(groups.length > 0, 'At least one handler has group selectors');

  const { sections } = render({ empty: true, report: actualReport }).snapshot();
  const rows = sections.filter(section => section.html.startsWith('<h2 class="handler-title">'))
    .flatMap(section => section.rows);
  assert.equal(rows.length, portrait.variants.length, 'Every variant retains its rendered row');
  const expectedTags = [
    ['group_sex', 'double_penetration'], ['group_sex', 'spitroast'], ['group_sex']
  ];
  groups.forEach(({ handler, index, variants }) => {
    assert.deepEqual(variants.map(({ variant }) => variant.tags), expectedTags,
      `Handler ${index}: exactly three group selectors in priority order`);
    assert.ok(handler.self.includes('receiver'), `Handler ${index}: receiver guard`);
    assert.ok(!handler.self.includes('doer'), `Handler ${index}: no doer guard`);
    variants.forEach(({ variant, v }) => {
      assert.deepEqual(variant.excl, [], `Variant ${v}: no selector exclusions`);
    });

    const memberships = variants.map(() => new Set());
    portrait.roots.forEach((root, rootIndex) => {
      const pools = variants.map(({ v }, selectorIndex) => {
        const images = new Set();
        root.slots.filter(slot => slot.v === v).forEach(slot => {
          slot.items.forEach(image => {
            images.add(image);
            memberships[selectorIndex].add(`${rootIndex}|${image}`);
          });
        });
        return images;
      });
      pools.slice(0, 2).forEach((images, selectorIndex) => {
        images.forEach(image => {
          assert.ok(pools[2].has(image),
            `Handler ${index}, root ${rootIndex}: ${expectedTags[selectorIndex].join(' + ')} image ${image} belongs to broad group pool`);
        });
      });
    });
    variants.forEach(({ v }, selectorIndex) => {
      const row = rows[v];
      const count = memberships[selectorIndex].size;
      assert.equal(row.label, expectedTags[selectorIndex].join(' + '), `Variant ${v}: rendered label`);
      assert.equal(row.links.length + row.placeholders.reduce((sum, box) => sum + box.count, 0),
        count, `Variant ${v}: links and repeated memberships cover the complete pool`);
      assert.equal(row.empty, count === 0, `Variant ${v}: empty state matches memberships`);
      assert.ok(row.meta.includes(count ? `<small>${count} images</small>` : '0 images in any folder'),
        `Variant ${v}: full membership count, including empty pools`);
    });
  });
});

if (actualReport) test('actual portrait report pools classifications into conditional state rows', () => {
  const expectedKeywords = [
    'confident', 'thinking', 'shy', 'awkward', 'sorry', 'disappointed', 'fear', 'pain',
    'resting', 'sleeping', 'happy', 'sad', 'angry', 'serious', 'surprised', 'relaxed'
  ];
  const portrait = actualReport.portrait;
  assert.ok(portrait && !portrait.error, 'Actual report has portrait usage data');
  const handlers = portrait.handlers.map((handler, index) => ({ handler, index }))
    .filter(({ handler }) => handler.folders.length === 1 && handler.folders[0] === 'portrait');
  assert.equal(handlers.length, 1, 'Exactly one portrait-only handler');
  const variants = portrait.variants.map((variant, index) => ({ variant, index }))
    .filter(({ variant }) => variant.h === handlers[0].index);
  assert.equal(variants.length, 17, 'Sixteen conditional states plus one neutral fallback');
  assert.deepEqual(variants.slice(0, -1).map(({ variant }) => variant.tags),
    expectedKeywords.map(keyword => [keyword]));
  assert.ok(variants.every(({ variant }) => variant.excl.length === 0), 'No state exclusions');
  assert.deepEqual(variants.at(-1).variant.tags, [], 'Final neutral variant is unconditional');
  assert.equal(variants.filter(({ variant }) => variant.tags.length === 0).length, 1,
    'Neutral is the only unconditional variant');

  const config = JSON.parse(fs.readFileSync(path.resolve(__dirname,
    '../../Mods/Assetpacks/Image_Generation_Config.json'), 'utf8'));
  const emotions = config.portrait_global.emotions;
  variants.forEach(({ variant }, rowIndex) => {
    const keyword = rowIndex < expectedKeywords.length ? expectedKeywords[rowIndex] : 'neutral';
    assert.deepEqual(variant.classifications, [...new Set(emotions[keyword])],
      `${keyword}: exported classifications match configured pool, including missing images`);
  });

  const snapshot = render({ empty: true, report: actualReport }).snapshot();
  const { sections } = snapshot;
  const portraitSections = sections.filter(section =>
    section.html.startsWith('<h2 class="handler-title"><b>portrait</b>'));
  assert.equal(portraitSections.length, 1, 'Exactly one rendered portrait section');
  const { rows } = portraitSections[0];
  assert.deepEqual(rows.map(row => row.label), [...expectedKeywords, '(no tags: always)'],
    'One row per condition and final neutral, with no classification-only rows');

  const all = actualReport.folders.flatMap(folder => (folder.items || []).map(item => ({
    cls: item.cls,
    href: (folder.base + item.path.split('/').map(encodeURIComponent).join('/'))
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
  })));
  const seen = new Set();
  const handlerRows = sections.filter(section => section.html.startsWith('<h2 class="handler-title">'))
    .flatMap(section => section.rows);
  assert.equal(handlerRows.length, portrait.variants.length, 'Every variant retains its row');
  portrait.variants.forEach((variant, index) => {
    const entries = new Map();
    portrait.roots.forEach((root, rootIndex) => {
      root.slots.filter(slot => slot.v === index).forEach(slot => {
        slot.items.forEach(globalIndex => {
          assert.ok(all[globalIndex], `Valid global image index ${globalIndex}`);
          entries.set(`${rootIndex}|${globalIndex}`, all[globalIndex]);
        });
      });
    });
    const row = handlerRows[index];
    const visible = [];
    let repeated = 0;
    entries.forEach(entry => {
      if (seen.has(entry.href)) repeated++;
      else { seen.add(entry.href); visible.push(entry.href); }
    });
    assert.deepEqual(row.links.map(link => link.href).sort(),
      visible.sort(), `${row.label}: only URLs not shown by any earlier handler variant`);
    assert.deepEqual(row.placeholders.map(box => box.count), repeated ? [repeated] : [],
      `${row.label}: one placeholder counts repeated memberships`);
    if (entries.size) assert.ok(row.meta.includes(`<small>${entries.size} images</small>`), row.label);
    const classes = variant.classifications || [...new Set([...entries.values()].map(entry => entry.cls).filter(Boolean))].sort();
    const escaped = (classes.length ? classes.join(', ') : '(none)')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    assert.equal(row.classifications, escaped, `${row.label}: complete classification metadata`);
  });
  rows.forEach((row, index) => {
    assert.ok(row.meta.includes(`<small>Variant ${index + 1}</small>`), row.label);
  });
  const hrefs = snapshot.rows.flatMap(row => row.links.map(link => link.href));
  assert.equal(new Set(hrefs).size, hrefs.length, 'No duplicate thumbnail href anywhere in handler view');
});
