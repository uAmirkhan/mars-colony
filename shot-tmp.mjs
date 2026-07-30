import { chromium } from '@playwright/test'
import { pathToFileURL } from 'node:url'

const file = process.argv[2]
const outDir = process.argv[3]

const browser = await chromium.launch()
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })

const errors = []
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()) })
page.on('pageerror', (e) => errors.push(String(e)))

await page.goto(pathToFileURL(file).href)
await page.waitForTimeout(400)

const sections = await page.$$('section')
for (let i = 0; i < sections.length; i++) {
  await sections[i].screenshot({ path: `${outDir}/s${i + 1}.png` })
}

// Пустые ссылки на символы: <use href="#x"> без соответствующего <symbol id="x">
const brokenUses = await page.evaluate(() => {
  const ids = new Set([...document.querySelectorAll('symbol[id]')].map((s) => s.id))
  const bad = new Set()
  for (const u of document.querySelectorAll('use')) {
    const h = (u.getAttribute('href') || '').replace('#', '')
    if (h && !ids.has(h)) bad.add(h)
  }
  return [...bad]
})

// Элементы, вылезшие за габарит холста
const overflow = await page.evaluate(() => {
  const out = []
  for (const c of document.querySelectorAll('.canvas')) {
    const cr = c.getBoundingClientRect()
    for (const el of c.querySelectorAll('*')) {
      const r = el.getBoundingClientRect()
      if (r.width === 0 && r.height === 0) continue
      if (r.right > cr.right + 1 || r.bottom > cr.bottom + 1 || r.left < cr.left - 1 || r.top < cr.top - 1) {
        out.push(`${el.className || el.tagName} вне холста`)
      }
    }
  }
  return [...new Set(out)]
})

// Тап-таргеты меньше 44x44
const smallTargets = await page.evaluate(() => {
  const out = []
  for (const el of document.querySelectorAll('.canvas button, .canvas .field, .canvas .crop-card, .canvas .field-buy')) {
    const r = el.getBoundingClientRect()
    if (r.width < 44 || r.height < 44) out.push(`${el.className}: ${Math.round(r.width)}x${Math.round(r.height)}`)
  }
  return [...new Set(out)]
})

console.log(JSON.stringify({ sections: sections.length, errors, brokenUses, overflow, smallTargets }, null, 2))
await browser.close()
