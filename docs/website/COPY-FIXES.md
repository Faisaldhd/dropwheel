# COPY-FIXES — mechanical replacements for `docs/site/index.html`

**Lane:** site-content · **Companion to:** `docs/website/CLAIMS.md` (row numbers below refer to it)
**Target:** `docs/site/index.html` (lead applies; this lane does not edit it)
**Rule for the lead:** each fix is one literal find-and-replace. The “key” line is a unique string near the
edit; the old/new pairs are exact, including `«»`, `—`, `→` and the Arabic diacritics. Replace only the
quoted fragment, not the surrounding element.

**Priority:** F1, F2, F3, F4 are the four false/overbroad claims (privacy, gesture, motion). F5 fixes a
broken instruction. F6–F9 are precision. F10–F31 are Arabic language quality and terminology.

**After applying, this must return nothing:**

```
grep -n "the update check you control\|Keep the key down\|No timers and no idle work\|بـTangerine\|تُعالَج كل شيء\|ملفات ارتباط\|مقيس في CI\|بثلاثة اتجاهات\|وتُقاس البقية عليه\|الصيغة مُصدَّرة\|هو المسار الناضج" docs/site/index.html
```

| Fix | Claim row | Lang | Kind | Severity |
|---|---|---|---|---|
| F1 | #37 | EN+AR | WRONG claim → precise | high (privacy) |
| F2 | #37/#39 | EN+AR | WRONG claim + Arabic grammar | high |
| F3 | #62 | EN+AR | WRONG claim (wrong gesture) + default-state disclosure | high |
| F4 | #47 | EN+AR | WRONG (overbroad) → scoped | medium |
| F5 | N7 | EN | broken verify instruction | medium |
| F6 | #6 | EN+AR | “verified” → “signed” | low |
| F7 | #4 | EN+AR | Windows 10 build floor | low |
| F8 | #44 | EN+AR | CSP wording | low |
| F9 | #78 | EN+AR | footer wording + Arabic calque | low |
| F10 | #45 | AR | bidi (`بـTangerine`) | medium |
| F11–F15 | #18, #25, #40 | AR | grammar/meaning | low |
| F16–F18 | #67, #68, #71 | AR | grammar/meaning | low |
| F19–F27 | #52, #72, #73, #48, #23, #70, #72, #74, #49 | AR | terminology/meaning | low |
| F28–F30 | #71, #69, #66 | AR | wording | low |
| F31 | #76 | EN+AR | hero precision | low |

---

## F1 — Privacy panel: the network claim is false (rows #37)

**Key:** `class="privacy-panel__lede"` (line 795)

**EN old**
`No account, no cloud conversion, no telemetry. The only network request is the update check you control.`

**EN new**
`No account, no cloud conversion, no telemetry. Outbound requests are limited to the update check, the optional FFmpeg/Pandoc downloads (off by default), and a file or link you drop on the wheel from a browser.`

**AR old**
`لا حساب ولا تحويل سحابي ولا قياسات. الطلب الشبكي الوحيد هو فحص التحديث الذي تتحكّم به.`

**AR new**
`لا حساب ولا تحويل سحابي ولا قياسات. تقتصر الطلبات الصادرة على فحص التحديث، وتنزيل FFmpeg وPandoc الاختياري (وهو معطّل افتراضيًا)، وملف أو رابط تُفلته على العجلة من المتصفح.`

> Why: `ToolInstaller.cs:24` (helper downloads), `DropReader.cs:25,233` (a dropped link is downloaded),
> `UpdateService.cs:21` (update check), and `AppSettings.cs:82` (`CheckForUpdates = true` — the check is on
> by default, so “you control” is not true as written). Keep the “Nothing is uploaded” claim: no upload path
> exists.

---

## F2 — FAQ “Are my files uploaded?”: same false absolute + Arabic grammar (rows #37/#39)

**Key:** `هل تُرفَع ملفاتي؟` (line 1002)

**EN old**
`No. Everything is processed locally; there is no account, telemetry or cloud conversion. The only network request is the update check described above.`

**EN new**
`No. Everything is processed locally; there is no account, telemetry or cloud conversion. Nothing you drop is uploaded. The only outbound requests are the update check described above, the optional FFmpeg/Pandoc downloads when you allow them, and a file or link you drop on the wheel from a browser.`

**AR old**
`لا. تُعالَج كل شيء محليًا، ولا حساب ولا قياسات ولا تحويل سحابي. الطلب الشبكي الوحيد هو فحص التحديث المذكور أعلاه.`

**AR new**
`لا. تُعالَج كل الملفات محليًا، ولا حساب ولا قياسات ولا تحويل سحابي، ولا يُرفَع أي شيء تُفلته. لا تخرج طلبات شبكية إلا لفحص التحديث المذكور أعلاه، أو لتنزيل FFmpeg وPandoc عند السماح به، أو لملف أو رابط تُفلته على العجلة من المتصفح.`

> Why: fixes the Arabic agreement error “تُعالَج كل شيء” (verb with no matching subject) at the same time.

---

## F3 — Wheel section: the page teaches the wrong gesture (row #62)

**Key:** `id="wheel-title"` block, the `section-lede` (lines 192–195)

**EN old**
`The wheel opens at the pointer while you drag. Keep the key down and tap another to switch wheels; drop on a slice to run it.`

**EN new**
`The wheel opens at the pointer while you drag, and stays open until you drop, so you can release the key and tap another one to switch wheels; drop on a slice to run it. The gesture is off on a fresh install — turn it on once in Settings.`

**AR old**
`تُفتح العجلة عند المؤشر أثناء السحب. أبقِ المفتاح مضغوطًا واضغط آخر لتبديل العجلة، وأفلت على قطاع لتنفيذه.`

**AR new**
`تُفتح العجلة عند المؤشر أثناء السحب، وتبقى مفتوحة حتى الإفلات، فيمكنك إفلات المفتاح والضغط على مفتاح آخر لتبديل العجلة، ثم الإفلات على قطاع لتنفيذه. الإيماءة معطّلة في التثبيت الجديد — شغّلها مرة واحدة من الإعدادات.`

> Why: `README.md:26` says the wheel “stays open until you drop, so you can **release the key** and tap
> another one to switch wheels”. The added sentence discloses the fresh-install default
> (`README.md:13`, `SECURITY.md:15`) without naming a sub-page — the two README lines disagree on whether it
> is “Company mode” or “Integration”, so “Settings” is the only wording that is safe today.

---

## F4 — What's new 08: “No timers and no idle work” is overbroad (row #47)

**Key:** `Motion you can turn off` / `حركة يمكن إيقافها` (line 882)

**EN old**
`One token set: 150 ms for the wheel, 180 ms for windows, and reduced motion honoured from Windows or the app. No timers and no idle work.`

**EN new**
`One token set: 150 ms for the wheel, 180 ms for windows, and reduced motion honoured from Windows or the app. No animation timers: when the app is idle, no storyboard runs.`

**AR old**
`مجموعة قيم واحدة: 150 مللي ثانية للعجلة و180 للنوافذ، مع احترام تقليل الحركة من ويندوز أو من التطبيق. بلا مؤقتات وبلا عمل خامل.`

**AR new**
`مجموعة قيم واحدة: 150 مللي ثانية للعجلة و180 للنوافذ، مع احترام تقليل الحركة من ويندوز أو من التطبيق. بلا مؤقتات للحركة: لا يعمل أي عرض حركي عندما يكون التطبيق خاملًا.`

> Why: `MOTION.md:9-11` and `:115-116` say exactly this (no animation on a timer, no animation work while
> idle). The absolute version is falsified by `RescuerWindows.cs:681` (a 60 s `DispatcherTimer`) and by the
> update scheduler at `App.cs:203`. Scoped to animation it is true and still concrete.

---

## F5 — Verify section names two different files; only one has a `.sig` (note N7)

**Key:** `Get-FileHash .\DropWheel-Setup.exe` (line 917)

**Old**
`<code>Get-FileHash .\DropWheel-Setup.exe -Algorithm SHA256</code>`

**New**
`<code>Get-FileHash .\DropWheel-2.3.0-Setup.exe -Algorithm SHA256</code>`

**Key:** `class="hash-block__label"` (line 960)

**Old**
`<span class="hash-block__label">SHA-256 · <bdi dir="ltr">DropWheel-Setup.exe</bdi></span>`

**New**
`<span class="hash-block__label">SHA-256 · <bdi dir="ltr">DropWheel-2.3.0-Setup.exe</bdi></span>`

> Why: the release publishes `DropWheel-2.3.0-Setup.exe.sig` but **no** `DropWheel-Setup.exe.sig`
> (`gh release view` asset list, CLAIMS §0). Step 2 already uses the versioned name (line 922), so this makes
> the three steps consistent and keeps `verify-signature.ps1` from failing on a missing `.sig`. The two
> files have the same SHA-256 (`7381cbd5…1d7156ce`), so no number changes.

---

## F6 — “verified” → “signed” (row #6)

**Key:** `class="hero-facts"` (lines 139–140)

**EN old** `<span data-lang="en"><bdi dir="ltr">v2.3.0</bdi> verified</span>`
**EN new** `<span data-lang="en"><bdi dir="ltr">v2.3.0</bdi> signed</span>`

**AR old** `<span data-lang="ar" lang="ar"><bdi dir="ltr">v2.3.0</bdi> موثَّق</span>`
**AR new** `<span data-lang="ar" lang="ar"><bdi dir="ltr">v2.3.0</bdi> موقَّع</span>`

**Key:** `class="release-card__label"` (line 930)

**EN old** `<span data-lang="en">Latest verified release</span>`
**EN new** `<span data-lang="en">Latest signed release</span>`

**AR old** `<span data-lang="ar" lang="ar">آخر إصدار موثَّق</span>`
**AR new** `<span data-lang="ar" lang="ar">آخر إصدار موقَّع</span>`

> Why: the artifact that proves the claim is the `.sig`; the page already says “last signed release” at
> line 61. Leave the UI string “Update could not be verified” (line 994) untouched — it is verbatim from
> `README.md:94`.

---

## F7 — Windows 10 build floor (row #4)

**Key:** `class="eyebrow"` in the hero (line 111)

**EN old** `Windows 10/11 · 64-bit · Free and open source`
**EN new** `Windows 10 (1809+) or 11 · 64-bit · Free and open source`

**AR old** `ويندوز 10/11 · 64 بت · مجاني ومفتوح المصدر`
**AR new** `ويندوز 10 (1809 أو أحدث) أو 11 · 64 بت · مجاني ومفتوح المصدر`

**Key:** `class="hero-facts"` (line 134)

> Apply the eyebrow pair above **first**: `Windows 10/11 · 64-bit` is a substring of the eyebrow string, so
> after key 1 is applied this pair matches only the hero fact.

**EN old** `Windows 10/11 · 64-bit`
**EN new** `Windows 10 (1809+) or 11 · 64-bit`

**AR old** `ويندوز 10/11 · 64 بت`
**AR new** `ويندوز 10 (1809 أو أحدث) أو 11 · 64 بت`

**Key:** `Windows 10 or 11, 64-bit. The installer is self-contained` (line 1014)

**EN old** `Windows 10 or 11, 64-bit. The installer is self-contained, so no .NET install.`
**EN new** `Windows 10 version 1809 or newer, or Windows 11; 64-bit. The installer is self-contained, so no .NET install.`

**AR old** `ويندوز 10 أو 11، 64 بت. المثبّت مكتفٍ ذاتيًا فلا حاجة لتثبيت .NET.`
**AR new** `ويندوز 10 إصدار 1809 أو أحدث، أو ويندوز 11؛ 64 بت. المثبّت مكتفٍ ذاتيًا فلا حاجة لتثبيت .NET.`

> Why: `README.md:96` — “Windows 10 (1809+) or Windows 11, x64”. Not a false claim today, just imprecise.

---

## F8 — CSP sentence: links are still clickable (row #44)

**Key:** `This page has no trackers` / `هذه الصفحة بلا متتبّعات` (line 807)

**EN old**
`No cookies, analytics, external fonts or scripts. The Content-Security-Policy blocks every outbound connection from the page.`

**EN new**
`No cookies, analytics, external fonts or scripts. The Content-Security-Policy blocks every outbound network request from the page; the GitHub links open only when you click them.`

**AR old**
`لا ملفات تعريف ارتباط ولا تحليلات ولا خطوط أو نصوص خارجية. تمنع سياسة أمان المحتوى كل اتصال صادر من الصفحة.`

**AR new**
`لا ملفات تعريف ارتباط ولا تحليلات ولا خطوط أو نصوص خارجية. تمنع سياسة أمان المحتوى كل طلب شبكي صادر من الصفحة؛ ولا تُفتح روابط GitHub إلا بنقرك عليها.`

> Why: the CSP (`index.html:11`) sets `connect-src 'none'` and `default-src 'self'`, which blocks
> subresources/XHR, but it does not and cannot block a user-clicked `<a href="https://github.com/…">`. The
> page has 20+ such links; the precise statement is “network request”.

---

## F9 — Footer: “no external requests” and the Arabic calque (row #78)

**Key:** `class="footer-privacy"` (line 1038)

**EN old** `No cookies · no analytics · no external requests`
**EN new** `No cookies · no analytics · no third-party requests`

**AR old** `لا ملفات ارتباط · لا تحليلات · لا طلبات خارجية`
**AR new** `لا ملفات تعريف الارتباط · لا تحليلات · لا طلبات خارجية`

> Why: “ملفات تعريف الارتباط” is the term the same page uses at line 807 and the Windows Arabic term;
> “ملفات ارتباط” is a calque. “third-party” is the precise English because the page does make requests to
> its own origin.

---

## F10 — Arabic bidi: `بـTangerine` (row #45)

**Key:** `هل هو مفتوح المصدر فعلًا؟` (line 1022)

**AR old**
`نعم. ترخيص MIT والشيفرة على GitHub. DropWheel مبني على FileAction من Metamorfolog وغير مرتبط بـTangerine.`

**AR new**
`نعم. ترخيص MIT والشيفرة على GitHub. DropWheel مبني على FileAction من Metamorfolog، ولا صلة له بمشروع Tangerine.`

**Key:** `class="footer-bottom"` (line 1071)

**AR old** `مشروع ويندوز مستقل. غير مرتبط بـTangerine.`
**AR new** `مشروع ويندوز مستقل. لا صلة له بمشروع Tangerine.`

> Why: the Arabic preposition “بـ” glued to a Latin word has no isolation character, so the bidi algorithm
> reorders the run (`glossary-ar.md:80-81` requires isolates around Latin values inside Arabic). Rewriting
> removes the glued preposition entirely; “Tangerine” stays untranslated as a proper noun. Do **not** add
> U+200E/U+200F here — the file deliberately uses `<bdi>` isolation instead.

---

## F11 — “الصيغة مُصدَّرة” (versioned ≠ exported) (row #18)

**Key:** `لماذا القفل تجريبي؟` (line 1010)

**AR old** `الصيغة مُصدَّرة وكل استبدال متحقَّق منه`
**AR new** `الصيغة تحمل رقم إصدار، وكل استبدال متحقَّق منه`

> Why: “مُصدَّرة” reads as “exported/issued”; “versioned” is “تحمل رقم إصدار”. The claim itself is proven by
> `FUNCTION-MATRIX.md:221-222` and the format constant in `LockFormat.cs`.

---

## F12 — Gender agreement: «حماية» is feminine (row #18)

**Key:** `لماذا القفل تجريبي؟` (line 1010)

**AR old** `و«حماية» (ZIP/PDF) هو المسار الناضج.`
**AR new** `و«حماية» (ZIP/PDF) هي المسار الناضج.`

---

## F13 — The quoted SmartScreen message is not the Arabic Windows string (row #16)

**Key:** `class="faq"` first `<summary>` (line 989)

**AR old** `يقول ويندوز «حماية Windows». ماذا أفعل؟`
**AR new** `يقول ويندوز «قام Windows بحماية جهازك». ماذا أفعل؟`

> Why: the English summary quotes the real UI string “Windows protected your PC”; the Arabic Windows
> SmartScreen string is «قام Windows بحماية جهازك», not «حماية Windows». Same fix keeps the EN block
> unchanged.

---

## F14 — Terse negation (row #40)

**Key:** `هل يحتاج صلاحيات المسؤول؟` (line 998)

**AR old** `لا للتثبيت الافتراضي. يضع`
**AR new** `لا، في التثبيت الافتراضي. يضع`

---

## F15 — “تُفلت عليه الملف” (row #37 area, FAQ results)

**Key:** `أين تُحفظ النتائج؟` (line 1006)

**AR old** `بجانب الأصل، أو في المجلد الذي تُفلت عليه الملف.`
**AR new** `بجانب الأصل، أو في المجلد الذي تُسقط عليه الملف.`

---

## F16 — “كل نافذة تكيّف حجمها” → “تُكيّف” (rows #3, #67) — **two occurrences**

**Key 1:** `class="section-note"` in `#tools` (line 259) · **Key 2:** what's-new item 01 (line 833)

**AR old** `كل نافذة تكيّف حجمها مع الوسائط`
**AR new** `كل نافذة تُكيّف حجمها مع الوسائط`

> Apply to both occurrences. `تكيّف` is intransitive (“it adapts”); the sentence needs the transitive
> passive “تُكيّف حجمها” (“its size is adapted”). Both blocks describe the same behaviour.

---

## F17 — “وتُضاف الأداة تلو الأخرى” (row #68)

**Key:** `class="section-note"` in `#tools` (line 259)

**AR old** `اللقطات من اختبار التطبيق الذاتي، وتُضاف الأداة تلو الأخرى.`
**AR new** `اللقطات من اختبار التطبيق الذاتي، وتُضاف أدوات جديدة تدريجيًا.`

---

## F18 — Join: “scaled and padded” is missing (row #71)

**Key:** `class="tool-num">09` card (line 421)

**AR old** `رتّب المقاطع بالسحب؛ يحدّد المقطع الأول الحجم ومعدل الإطارات، وتُقاس البقية عليه.`
**AR new** `رتّب المقاطع بالسحب؛ يحدّد المقطع الأول الحجم ومعدل الإطارات، وتُقاس بقية المقاطع على حجمه وتُضاف إليها حواف سوداء.`

> Why: `VideoSplitJoin.cs:278-279` — “the first clip's size and frame rate are the output's, every other clip
> is scaled … inside that frame and padded with black”.

---

## F19 — Speed: pitch is “طبقة الصوت”, not “ارتفاعها” (row #52)

**Key:** `class="tool-num">10` card (line 435)

**AR old** `من 0.25× إلى 4× مع الحفاظ على طبقة الصوت وارتفاعها. المقطع الصامت لا يمثّل مشكلة.`
**AR new** `من 0.25× إلى 4× مع الحفاظ على طبقة الصوت. المقطع الصامت لا يمثّل مشكلة.`

> Why: dropping “وارتفاعها” fixes the meaning (the audio is pitch-preserved by the atempo chain,
> `VideoEdit.cs:183-186`); the redundant second noun made it read as if two properties were kept.

---

## F20 — Visualizer: “three shapes” are frames, not directions (row #72)

**Key:** `class="tool-num">18` card (line 547)

**AR old** `على خلفية سوداء أو صورة، بثلاثة اتجاهات.`
**AR new** `على خلفية سوداء أو صورة، بثلاثة أُطُر: أفقي ورأسي ومربّع.`

> Why: the three choices are `Landscape / Portrait / Square` (`en.json:884-886`), i.e. frames (“أُطُر”),
> not directions.

---

## F21 — Merge: “يُحفظ تدوير الصفحات” (row #73)

**Key:** `class="tool-num">19` card (line 561)

**AR old** `ورتّبها بالأسهم؛ يُحفظ تدوير الصفحات ونصها.`
**AR new** `ورتّبها بالأسهم؛ يُحافَظ على تدوير الصفحات ونصها.`

---

## F22 — Audio trim: “قصّ صامت الأطراف” (row #72)

**Key:** `class="tool-num">14` card (line 491)

**AR old** `وقصّ صامت الأطراف تلقائيًا`
**AR new** `وقصّ الأطراف الصامتة تلقائيًا`

---

## F23 — “مقيس في CI” (row #48)

**Key:** what's-new item 09 heading (line 888)

**AR old** `مقيس في CI`
**AR new** `قياس داخل CI`

---

## F24 — CPU is “المعالج”, not “وحدة المعالجة” in this context (row #48)

**Key:** what's-new item 09 body (line 889)

**AR old** `وبوابة حركة تفحص زمن الإطار ووحدة المعالجة والاحتفاظ بالذاكرة في كل تشغيل.`
**AR new** `وبوابة حركة تفحص زمن الإطار واستهلاك المعالج والاحتفاظ بالذاكرة في كل تشغيل.`

> Why: the three gates in `PERF.md:7-9` are frame p95, idle **CPU** and retention; “وحدة المعالجة” is the
> hardware unit, “استهلاك المعالج” is what is measured.

---

## F25 — “بقائمته البيضاء” (row #16 FAQ)

**Key:** `إذا تطابقا، اختر` (line 990)

**AR old** `ويمكن لفريق الأمان إدراج الملف بقائمته البيضاء بالبصمة.`
**AR new** `ويمكن لفريق الأمان إدراج الملف في قائمة السماح بالبصمة.`

---

## F26 — Crop must be “قصّ”, not “اقتصاص”, to match the app (row #70)

The app distinguishes the two operations: `ar.json:99` `"tool.crop.title": "قصّ الصورة"`,
`ar.json:919` `"tool.videocrop.title": "قصّ الفيديو"`, while Trim is `اقتصاص`
(`ar.json:76` `"tool.audiotrim.title": "اقتصاص الصوت"`, `ar.json:900` `"tool.videotrim.title": "اقتصاص الفيديو"`).
The page currently calls both Crop cards “اقتصاص”.

**Key 1:** the Photos card whose kicker is `<span class="tool-num">01</span>` (line 288)

**AR old** `<h3><span data-lang="en">Crop</span><span data-lang="ar" lang="ar">اقتصاص</span></h3>`
**AR new** `<h3><span data-lang="en">Crop</span><span data-lang="ar" lang="ar">قصّ</span></h3>`

**Key 2:** the Video card whose kicker is `<span class="tool-num">11</span>` (line 448) — same old string
**AR old** `<h3><span data-lang="en">Crop</span><span data-lang="ar" lang="ar">اقتصاص</span></h3>`
**AR new** `<h3><span data-lang="en">Crop</span><span data-lang="ar" lang="ar">قصّ</span></h3>`

**Key 3:** the Photos Crop alt text (line 280)

**AR old** `نافذة أداة الاقتصاص: مستطيل قصّ فوق صورة، وشرائح نسب، ومربعا العرض والارتفاع.`
**AR new** `نافذة أداة قصّ الصورة: مستطيل قصّ فوق صورة، وشرائح نسب، ومربعا العرض والارتفاع.`

> The two `<h3>` strings are identical, so use the `tool-num` (`01` / `11`) to pick the card; both get the
> same replacement. Do not touch the two Trim cards (audio `14`, video `07`), which correctly say
> “اقتصاص”.

---

## F27 — “صوت حتى أدق تفاصيل الموجة” (row #72 area)

**Key:** what's-new item 04 heading (line 853)

**AR old** `صوت حتى أدق تفاصيل الموجة`
**AR new** `الصوت حتى أدقّ تفاصيل الموجة`

> “الصوت” with the article is the established term (`glossary-ar.md:25`) and the sentence needs a subject;
> “أدقّ” takes the shadda because it is the elative.

---

## F28 — What's new 03: “وسرعة” and the duplicated “قصّ إطار” (row #71)

**Key:** what's-new item 03 body (line 847)

**AR old** `اقتصاص وتقسيم ودمج وسرعة وقصّ إطار وحفظ إطارات وحجب فيديو — نسخ البثّ متى أمكن وإعادة الترميز عند الحاجة.`
**AR new** `اقتصاص وتقسيم ودمج والسرعة وقصّ الفيديو وحفظ الإطارات وحجب الفيديو — نسخ البثّ متى أمكن وإعادة الترميز عند الحاجة.`

> Why: the tool is “السرعة” (`glossary-ar.md:59`), the Crop tool is “قصّ الفيديو” (`ar.json:919`), and the
> other nouns take the article to read as a list of tools rather than a sentence fragment.

---

## F29 — “بلا تصميمات وهمية” (row #69)

**Key:** `class="hero-art__caption"` (line 181)

**AR old** `لقطة من التطبيق المُسلَّم نفسه. بلا تصميمات وهمية.`
**AR new** `لقطة من التطبيق المُسلَّم نفسه. بلا نماذج وهمية.`

> Why: “mock-up” is “نموذج” in this sense; “تصميم” means design.

---

## F30 — “وEnter” bidi join (row #66)

**Key:** `class="wheel-notes"` third item (line 245)

**AR old** `والأسهم وEnter.`
**AR new** `والأسهم ومفتاح Enter.`

> Why: “و” glued to a Latin key name has no isolation; adding the Arabic noun separates the runs and matches
> the glossary's “تُترك أسماء المفاتيح … كما هي” (`glossary-ar.md:86`).

---

## F31 — Hero lede: “on what you need” → “on the tool you need” (row #76)

**Key:** `class="lede"` (lines 116–117)

**EN old** `Grab a file, tap a key, drop it on what you need. Convert, edit, protect and lock — without opening an app.`
**EN new** `Grab a file, tap a key, drop it on the tool you need. Convert, edit, protect and lock — without opening an app.`

**AR old** `اسحب ملفًا، واضغط مفتاحًا، وأفلته على ما تريد. تحويل وتحرير وحماية وقفل — دون فتح تطبيق.`
**AR new** `اسحب ملفًا، واضغط مفتاحًا، وأفلته على الأداة التي تريد. تحويل وتحرير وحماية وقفل — دون فتح تطبيق.`

> “on what you need” is vague; the wheel is a set of tools/slices, so “the tool you need” is both truer and
> shorter in Arabic.

---

## Not fixes (deliberately left alone)

* **“26” everywhere** — correct; the brief's 25 is the stale number (`CLAIMS.md` §0).
* **“60.05 MB”** — correct as MiB; the exact byte count sits beside it. Do not convert to 62.97 MB.
* **“Update could not be verified”** — verbatim UI string (`README.md:94`).
* **“AES-256”, “ECDSA P-256”, “24 characters”, “150 ms / 180 ms”, “sixteen”, “nine” ×2, “1080² /
  1920×1080 / 1080×1920”, “0.25× to 4×”, “60 seconds”** — all proven; see `CLAIMS.md` rows #7–#9, #19–#20,
  #27, #33–#34, #46, #49–#52.
* **Format lists (8 cards)** — proven against `README.md:41-49` plus `Catalog.cs:231` for PDF→XLSX.
* **“Rendered from the shipped app. No mock-ups.”** — true of the hero and of the 5 real tool captures; the
  grid's decorative tiles are already disclosed at line 258. Do not extend the caption to the whole grid.
