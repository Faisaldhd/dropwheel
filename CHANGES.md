# DropWheel 2.4.0 — CHANGES

## 2.4.0

### English

**26 tool windows, one gesture.** Choosing a tool on the Tools wheel (Ctrl) no longer starts a
background job when the work needs eyes on it: it opens a floating card over the file. 25 of the 26
windows are new in this release (Crop arrived in 2.3.0); each one writes a new file — the QR reader
writes none — and leaves the source alone, except Lock, which transforms the item in place.

#### Images

- **Annotate Image — `tools.annotate`.** Draw arrows, lines, shapes, text, numbered callouts, a
  magnifier, freehand pen/highlighter strokes and stamp images over a downsampled preview; the flattened
  copy is written at the original resolution as `<name> Annotated.<ext>`. Every mark is stored in
  original-image pixels, so resizing the window never moves it.
- **Edit Photos — `tools.editphotos`.** Sixteen sliders in five groups (Light, Color, Detail, Effects,
  Noise) over a live preview that never blocks the window: a 24 MP photo refreshes in ~350 ms and the
  save renders the original. With every slider at its default the result is a **byte-identical copy** of
  the source — no recompression, no orientation rewrite — and the name is `<name> Edited.<ext>`.
- **Add BG — `tools.addbackground`.** Padding, rounded corners, shadow and a backdrop of nine gradients,
  nine solids, a custom colour or an image, then 1:1 / 16:9 / 4:5 / square crops of the composition.
  Slider values are original-image pixels; the result is `<name> With Background.png` (PNG, alpha kept).
- **Collage — `tools.collage`.** Two or more photos in a grid at 1080 × 1080, 1920 × 1080 or 1080 × 1920,
  rendered from the sources (never from the 640 px preview) as `Collage.png` next to the first input; the
  inputs keep their bytes and timestamps.
- **Redact image — `tools.redact`.** Draw the areas to hide and cover each with a solid fill, a blur or
  pixelation, or let the Windows OCR engine propose the text areas; the flattened copy is
  `<name> Redacted.<ext>`.
- **Crop Image** from 2.3.0 keeps its corner/edge handles, ratio chips and W/H boxes in original pixels.

#### Video

- **Trim Video, Crop Video, Change Speed — `tools.videotrim` / `tools.videocrop` / `tools.videospeed`.**
  A frame preview, a filmstrip and a timeline; trim a range, crop the frame, or change the speed with the
  pitch kept. Outputs are `<name> Trimmed.<ext>`, `<name> Cropped.<ext>` and `<name> <rate>x.<ext>`.
- **Split Video and Join Videos — `tools.videosplit` / `tools.videojoin`.** Click a timeline to add split
  points (drag to move, double-click to remove) and get `<name> Parts` with numbered parts, or reorder
  clips by dragging and join them into `Joined.mp4`; the first clip defines size and frame rate, a clip
  without audio gets silence, and stream copy is used when the container allows it.
- **Save frames — `tools.videoframes`.** Scrub, step one frame at a time and capture stills; every
  captured frame is written at full resolution as `<name> frame 001.png` in a new `<name> frames` folder.
  The button counts what it will write ("Save 3 Frames").
- **Redact video — `tools.videoredact`.** Drag a rectangle over what must be hidden, give each area its
  own time range and choose a solid colour or a blur; the whole result is one FFmpeg filter graph and
  `<name> Redacted.<ext>`.
- Video tools keep MP4, MOV, MKV and WebM and write legacy containers (AVI, WMV, FLV, MPG, 3GP, TS) as
  MP4. A cancelled or failed run leaves no temporary file and no partial result.

#### Audio

- **Trim Audio — `tools.audiotrim`.** The real waveform with draggable orange Start/End handles, exact
  time boxes, an automatic silent-ends trim and playback of the selection; output `<name> Trimmed.<ext>`.
- **Normalize Audio — `tools.audionormalize`.** An EBU R128 target, an analysis pass that measures the
  source and predicts the result on the waveform, then a two-pass apply into `<name> Normalized.<ext>`.
- **Audio Channels — `tools.audiochannels`.** Mono or stereo, two channel volumes and a solo, with the
  real waveform in view; output `<name> Mono.<ext>` or `<name> Stereo.<ext>`.
- **Bleep Audio — `tools.audiobleep`.** Drag sections on the Bleep layer above the waveform, resize them,
  set the tone in Hz and its volume, preview "With Bleeps", and save `<name> Bleeped.<ext>`.
- **Audio Visualizer — `tools.audiovisualizer`.** Turn audio into an MP4 with an animated waveform or a
  vector scope, on black or over a still image, in landscape, portrait or square:
  `<name> Visualizer.mp4`.
- Audio tools keep the source format when FFmpeg can write it (WMA becomes MP3) and need FFmpeg; without
  it their slices stay on the wheel dimmed with the helper-tool prompt.

#### Documents

- **Merge to PDF — `tools.pdfmerge`.** Two or more PDFs and/or images in one `Merged.pdf`, in the order
  you set with the up/down arrows; page rotation is preserved, one image is exactly one page, and a
  corrupt or password-protected source fails with a message instead of a half-written file.
- **Organize Pages — `tools.pdforganize`.** A numbered card per page: drag to reorder, rotate, duplicate,
  remove (never the last page), and an optional "Make all pages the same width"; output
  `<name> Organized.pdf`, source untouched.

#### Any file

- **Compress File — `tools.compress`.** One image, audio file, video or PDF, by preset, target size or
  percentage, with an optional cap on the image's long side. Output `<name> Compressed.<ext>`; video and
  audio keep their duration and audio track; a target size is a goal the encoder lands within about 10 %
  above, and image compression never upscales.
- **Edit Metadata — `tools.metadata`.** Searchable sections (File, Image, Video track, Audio track,
  ExifIFD, IPTC, XMP, PDF info, Chapters) with editable rows first, per-field removal, chapters as
  `mm:ss Title`, and one **Remove All** that also strips hidden camera and processing data. Output
  `<name> Edited metadata.<ext>`.

#### Protection and privacy

- **Password protection — `tools.protect`.** One file, one folder or several files become
  `<name> Protected.zip` with WinZip AES-256 entry encryption, or `<name> Protected.pdf` with AES-256
  (PDF 2.0) when a single PDF is selected. Two password fields, a match line, a strength meter and an
  8-character minimum; the password is never stored, and the window states the two limits that matter —
  a ZIP cannot hide its file names, and PDF owner permissions are enforced by the reader, not by
  DropWheel.
- **Remove password — `tools.unlock`.** One protected ZIP or PDF becomes `<name> Unlocked.zip` /
  `<name> Unlocked.pdf`; a wrong password or a cancelled run leaves no partial file, and an unencrypted
  ZIP is reported as not protected instead of being rewritten. Password recovery is not attempted.
- **Lock — `tools.lock` (Beta).** A file or folder is transformed **in place** into one self-contained
  `.dwenc` / `.dwdir`: `report.pdf` becomes `report.pdf.dwenc`, `Photos` becomes `Photos.dwdir`. It ships
  a three-slot envelope (file password, Rescuer password, Rescuer recovery key shown once), an
  authenticated header, chunked AES-256-GCM frames, a verified atomic replacement, a folder
  `desktop.ini` badge with the purple two-tone padlock, auto-relock and crash recovery. No password or
  key is written under `%LOCALAPPDATA%\DropWheel`, the copy left in the Recycle Bin after an unlock is
  off by default, and the registry work stays per user (HKCU).

#### Tool windows, wheel and motion

- **One floating tool, 26 times.** Each tool lives in its own file and registers itself; the registry is
  the single list, and a slice opens its own window by tool id, so Crop, Annotate and Edit Photos never
  compete for the same photo.
- **Windows adapt to the media.** A tool opens at its default size, probes the file asynchronously and
  animates in 180 ms to a size that fits the media's aspect ratio inside the tool's preview bounds,
  clamped to 95 % of the work-area width and 90 % of its height on the monitor under the cursor; tiny
  media is never enlarged past 200 %. Preview bounds, chrome size and minimum panel live with the tool.
- **Speed and colour of the chrome.** Each tool states its own verb on the primary button ("Create
  Collage", "Merge PDFs", "Save Edited Copy", "Encrypt"…), **Reset** is always on the left, **Esc**
  closes, **Enter** runs the primary action, the title bar drags the card, and a finished result is
  announced with a "Saved" notification that names the file. Tools use the theme brushes only and retint
  on a live dark/light switch.
- **Motion is a token set, not decoration.** One file (`UI/Motion/Motion.cs`) owns every duration and
  easing; only `RenderTransform` and `Opacity` animate (the single exception is the 180 ms media resize),
  nothing polls, and with **Settings → General → Animations** off — or Windows' animation effects off —
  every transition is instant with no idle cost.
- **Nothing is overwritten.** Results go to the folder the drop resolved (2.2.3 rules) or next to the
  source, and a second result becomes `<name> (2).<ext>`.

#### Performance and quality

- The `--self-test` motion gate fails CI when the UI frame p95 exceeds **16.7 ms**, idle CPU exceeds
  **5 %** or the motion surfaces retain more than **12 MB** after closing. The recorded run on the merge
  head: UI frame p95 **0.02–0.07 ms**, idle CPU **0.39 %**, retained **0.0 MB** (`docs/PERF.md`).
- Compress caps its video two-pass average with `maxrate`/`bufsize` so a target size is reached honestly,
  and a cancelled video run cleans its temporary files without touching the source.
- Both string tables carry **1,094 keys each** with identical key sets and placeholders, enforced by the
  localization tests in CI; the self-test renders the app in English and Arabic, light and dark.

#### Not in this release

- **Redact image has no face detection.** Manual areas and OCR text detection ship; a face detector needs
  either the Windows SDK projection this build does not carry or a new third-party package.
- **QR decoding is read-only** and only `http`/`https` can be opened from a code.
- **Lock is Beta** until the owner signs the container format off.
- The binaries are still Authenticode-unsigned (SmartScreen may ask once); auto-updates are separately
  signed with ECDSA P-256.
- Both string tables are complete and enforced (1,094 keys each, identical key sets and placeholders,
  parity tests in CI) and the self-test renders the shipped build. The per-window bilingual rendering
  gate — every tool window in English and Arabic at 100/150/200 % DPI — is still landing in
  `audit/selftest`; until it is green, no claim is made that every window has been rendered in both
  languages.

### العربية

**⁦26⁩ نافذة أدوات بإيماءة واحدة.** لم يعد اختيار أداة من عجلة الأدوات (Ctrl) يبدأ مهمة في الخلفية حين
يحتاج العمل إلى نظر: صار يفتح بطاقة عائمة فوق الملف. خمس وعشرون من النوافذ الست والعشرين جديدة في هذا
الإصدار (وصلت أداة قصّ الصورة في ⁦2.3.0⁩)، وكلها تكتب ملفًا جديدًا وتترك الأصل سليمًا، إلا «قفل الملفات»
الذي يحوّل العنصر في مكانه.

#### الصور

- **إضافة تعليقات على الصورة — `tools.annotate`.** ارسم أسهمًا وخطوطًا وأشكالًا ونصوصًا وتعليقات مرقّمة
  وعدسة مكبّرة وخطوط قلم حرّ وتظليلًا وأختام صور فوق معاينة مصغّرة، وتُكتب النسخة المسطّحة بدقة الصورة
  الأصلية باسم `<الاسم> Annotated.<الامتداد>`. تُحفظ كل علامة ببكسلات الصورة الأصلية، فلا تحرّكها إعادة
  تحجيم النافذة.
- **تعديل الصور — `tools.editphotos`.** ستة عشر شريط تمرير في خمس مجموعات (الضوء، اللون، التفاصيل،
  المؤثرات، الضجيج) فوق معاينة حيّة لا تجمّد النافذة: صورة بدقة ⁦24⁩ ميغابكسل تتحدّث في نحو ⁦350⁩ مللي
  ثانية، والحفظ يعالج الأصل. وعندما تكون كل الشرائط على وضعها الافتراضي تكون النتيجة **نسخة مطابقة بايتًا
  ببايت** من المصدر، بلا إعادة ضغط ولا إعادة كتابة لاتجاه EXIF، والاسم `<الاسم> Edited.<الامتداد>`.
- **إضافة خلفية — `tools.addbackground`.** حشوة وزوايا مستديرة وظلّ وخلفية من تسعة تدرّجات أو تسعة ألوان
  صريحة أو لون مخصّص أو صورة، ثم قصّ التكوين بنسب ⁦1:1⁩ و⁦16:9⁩ و⁦4:5⁩ والمربّع. قيم الشرائط ببكسلات
  الصورة الأصلية، والناتج `<الاسم> With Background.png` (صيغة PNG مع الحفاظ على الشفافية).
- **تجميع الصور — `tools.collage`.** صورتان أو أكثر في شبكة بمقاس ⁦1080 × 1080⁩ أو ⁦1920 × 1080⁩ أو
  ⁦1080 × 1920⁩، تُرسم من المصادر لا من معاينة ⁦640⁩ بكسل، وتُكتب `Collage.png` بجانب أول ملف، مع بقاء
  بايتات المصادر وتواريخ تعديلها كما هي.
- **حجب الصورة — `tools.redact`.** ارسم المناطق التي تريد إخفاءها وغطِّ كل واحدة بتعبئة صلبة أو ضباب أو
  بكسلة، أو دع محرّك التعرف الضوئي في Windows يقترح مناطق النص؛ والنسخة المسطّحة `<الاسم> Redacted.<الامتداد>`.
- **قصّ الصورة** من ⁦2.3.0⁩ يحتفظ بمقابض الزوايا والحواف وشرائح النسب ومربّعَي العرض والارتفاع ببكسلات الأصل.

#### الفيديو

- **اقتصاص الفيديو وقصّ الفيديو وتغيير السرعة — `tools.videotrim` و`tools.videocrop` و`tools.videospeed`.**
  معاينة إطار وشريط إطارات وخط زمني؛ اقتصّ مقطعًا، أو اقصص الإطار، أو غيّر السرعة مع الحفاظ على حدة الصوت.
  النواتج `<الاسم> Trimmed.<الامتداد>` و`<الاسم> Cropped.<الامتداد>` و`<الاسم> <المعدّل>x.<الامتداد>`.
- **تقسيم الفيديو ودمج الفيديوهات — `tools.videosplit` و`tools.videojoin`.** انقر على الخط الزمني لإضافة
  نقاط تقسيم (اسحبها لتحريكها وانقر نقرًا مزدوجًا لحذفها) لتحصل على مجلد `<الاسم> Parts` بأجزاء مرقّمة، أو
  أعد ترتيب المقاطع بالسحب ثم ادمجها في `Joined.mp4`؛ أول مقطع يحدّد المقاس ومعدّل الإطارات، والمقطع الذي
  بلا صوت يحصل على صمت، ويُستخدم النسخ المباشر حين تسمح الحاوية بذلك.
- **حفظ الإطارات — `tools.videoframes`.** حرّك المؤشر وتنقّل إطارًا بإطار والتقط اللقطات؛ يُكتب كل إطار
  ملتقط بالدقة الكاملة باسم `<الاسم> frame 001.png` في مجلد جديد `<الاسم> frames`. ويعرض الزر عدد ما
  سيُكتب («حفظ ⁦3⁩ إطارات»).
- **حجب الفيديو — `tools.videoredact`.** اسحب مستطيلًا فوق ما يجب إخفاؤه، وأعطِ كل منطقة مداها الزمني
  واختر تعبئة صلبة أو ضبابًا؛ والنتيجة كلها مخطط مرشّحات واحد في FFmpeg وملف `<الاسم> Redacted.<الامتداد>`.
- تحتفظ أدوات الفيديو بحاويات MP4 وMOV وMKV وWebM وتكتب الحاويات القديمة (AVI وWMV وFLV وMPG و3GP وTS)
  بصيغة MP4. والتشغيل الملغى أو الفاشل لا يترك ملفًا مؤقتًا ولا نتيجة ناقصة.

#### الصوت

- **اقتصاص الصوت — `tools.audiotrim`.** الموجة الحقيقية بمقبضَي بداية ونهاية برتقاليين قابلين للسحب،
  ومربّعا وقت دقيقان، وخيار اقتصاص الصمت الطرفي تلقائيًا، وتشغيل المقطع المحدّد؛ والناتج
  `<الاسم> Trimmed.<الامتداد>`.
- **تطبيع الصوت — `tools.audionormalize`.** هدف ⁦EBU R128⁩، ثم تحليل يقيس المصدر ويتوقّع النتيجة على
  الموجة، ثم تطبيق بمرورين إلى `<الاسم> Normalized.<الامتداد>`.
- **قنوات الصوت — `tools.audiochannels`.** أحادي أو ستيريو، ومستويا صوت للقناتين، وعزل قناة، مع الموجة
  الحقيقية أمامك؛ والناتج `<الاسم> Mono.<الامتداد>` أو `<الاسم> Stereo.<الامتداد>`.
- **استبدال الصوت بصافرة — `tools.audiobleep`.** اسحب مقاطع على طبقة الصافرة فوق الموجة، وغيّر أحجامها،
  واضبط النغمة بالهرتز ومستواها، وعاين «مع الصافرات»، ثم احفظ `<الاسم> Bleeped.<الامتداد>`.
- **مُصوِّر الصوت — `tools.audiovisualizer`.** حوّل الصوت إلى ⁦MP4⁩ بموجة متحركة أو بمؤشر متجهي، على
  خلفية سوداء أو فوق صورة ثابتة، بأبعاد عرضية أو طولية أو مربّعة: `<الاسم> Visualizer.mp4`.
- تحتفظ أدوات الصوت بصيغة المصدر حين يستطيع FFmpeg كتابتها (فيصبح ⁦WMA⁩ ملف ⁦MP3⁩)، وتحتاج FFmpeg؛ وبدونه
  تبقى قطاعاتها باهتة على العجلة مع دعوة تنزيل الأداة.

#### المستندات

- **الدمج إلى PDF — `tools.pdfmerge`.** ملفا PDF أو أكثر و/أو صور في `Merged.pdf` واحد بالترتيب الذي
  تضبطه بالأسهم؛ يُحفظ تدوير كل صفحة، والصورة الواحدة صفحة واحدة بالضبط، والمصدر التالف أو المحمي بكلمة
  مرور يفشل برسالة بدل ملف نصف مكتوب.
- **تنظيم الصفحات — `tools.pdforganize`.** بطاقة مرقّمة لكل صفحة: اسحب لإعادة الترتيب، ودوّر، وكرّر، واحذف
  (لا آخر صفحة أبدًا)، مع خيار «جعل كل الصفحات بالعرض نفسه»؛ والناتج `<الاسم> Organized.pdf` والأصل سليم.

#### أي ملف

- **ضغط الملف — `tools.compress`.** صورة أو ملف صوت أو فيديو أو PDF واحد، بإعداد جاهز أو حجم مستهدف أو
  نسبة مئوية، مع خيار تحديد حدّ أقصى للضلع الأطول في الصور. الناتج `<الاسم> Compressed.<الامتداد>`؛
  ويحتفظ الفيديو والصوت بالمدّة ومسار الصوت، والحجم المستهدف هدف يبلغه المرمّز في حدود ⁦10⁩٪ أعلى منه
  تقريبًا، وضغط الصور لا يكبّرها أبدًا.
- **تعديل البيانات الوصفية — `tools.metadata`.** أقسام قابلة للبحث (الملف، الصورة، مسار الفيديو، مسار
  الصوت، ⁦ExifIFD⁩، ⁦IPTC⁩، ⁦XMP⁩، معلومات PDF، الفصول) مع تقديم الصفوف القابلة للتحرير، وإزالة حقل
  بحقل، والفصول بصيغة `mm:ss العنوان`، وزر **إزالة الكل** الذي يمحو أيضًا بيانات الكاميرا والمعالجة
  المخفية. الناتج `<الاسم> Edited metadata.<الامتداد>`.

#### الحماية والخصوصية

- **الحماية بكلمة مرور — `tools.protect`.** ملف واحد أو مجلد واحد أو عدة ملفات تصبح
  `<الاسم> Protected.zip` بتشفير ⁦AES-256⁩ على مستوى العنصر (WinZip)، أو `<الاسم> Protected.pdf` بتشفير
  ⁦AES-256⁩ (⁦PDF 2.0⁩) عند اختيار ملف ⁦PDF⁩ واحد. حقلان لكلمة المرور وسطر مطابقة ومقياس قوة وحدّ أدنى
  من ⁦8⁩ أحرف؛ ولا تُحفظ كلمة المرور، وتذكر النافذة الحدّين المهمين: الأرشيف لا يخفي أسماء ملفاته، وأذونات
  مالك ⁦PDF⁩ يطبّقها القارئ لا DropWheel.
- **إزالة كلمة المرور — `tools.unlock`.** ملف ⁦ZIP⁩ أو ⁦PDF⁩ محمي واحد يصبح `<الاسم> Unlocked.zip` أو
  `<الاسم> Unlocked.pdf`؛ وكلمة المرور الخاطئة أو التشغيل الملغى لا يتركان ملفًا ناقصًا، والأرشيف غير
  المشفّر يُبلَّغ عنه بأنه غير محمي بدل إعادة كتابته. ولا تُحاول الأداة استعادة كلمة المرور.
- **قفل الملفات — `tools.lock` (إصدار تجريبي).** يُحوَّل الملف أو المجلد **في مكانه** إلى حاوية واحدة
  مكتفية ذاتيًّا بامتداد ⁦.dwenc⁩ أو ⁦.dwdir⁩: يصبح `report.pdf` ملف `report.pdf.dwenc`، ويصبح `Photos`
  مجلد `Photos.dwdir`. وتحمل الحاوية مغلّفًا بثلاثة مداخل (كلمة سر الملف، وكلمة سر المنقذ، ومفتاح المنقذ
  الذي يظهر مرة واحدة)، وترويسة موثّقة، وإطارات ⁦AES-256-GCM⁩ مقسّمة، واستبدالًا ذرّيًّا موثّقًا، وشارة
  `desktop.ini` للمجلد بقفل بنفسجي بلونين، وإعادة قفل تلقائية واستردادًا بعد الانهيار. ولا تُكتب كلمة سر
  ولا مفتاح تحت `%LOCALAPPDATA%\DropWheel`، ونسخة سلة المحذوفات متوقفة افتراضيًّا، والعمل على السجل يبقى
  للمستخدم الحالي (⁦HKCU⁩).

#### نوافذ الأدوات والعجلة والحركة

- **أداة عائمة واحدة، ⁦26⁩ مرة.** كل أداة في ملفها وتُسجّل نفسها؛ والسجل هو القائمة الوحيدة، ويفتح القطاع
  نافذته بحسب معرّف الأداة، فلا تتنافس أدوات القصّ والتعليقات وتعديل الصور على الصورة نفسها.
- **النوافذ تتكيّف مع الوسائط.** تُفتح الأداة بحجمها الافتراضي، وتفحص الملف في الخلفية، ثم تتحرك في ⁦180⁩
  مللي ثانية إلى حجم يناسب نسبة أبعاد الوسائط داخل حدود معاينتها، مقيَّدًا بـ⁦95⁩٪ من عرض مساحة العمل
  و⁦90⁩٪ من ارتفاعها على شاشة المؤشر؛ ولا تُكبَّر الوسائط الصغيرة أكثر من ⁦200⁩٪. وحدود المعاينة وحجم
  الهيكل وأصغر لوحة تبقى مع الأداة نفسها.
- **سرعة الهيكل ولونه.** تُعلن كل أداة فعلها على الزر الأساسي («إنشاء تجميع»، «دمج ملفات PDF»،
  «حفظ نسخة معدّلة»، «تشفير»…)، و**إعادة ضبط** على اليسار دائمًا، و**Esc** يغلق، و**Enter** ينفّذ الإجراء
  الأساسي، وشريط العنوان يسحب البطاقة، وتُعلَن النتيجة المحفوظة بإشعار «تم الحفظ» يذكر اسم الملف. وتستخدم
  الأدوات فرش السمة وحدها وتتبع تبديل الوضع الفاتح/الداكن الحي.
- **الحركة مجموعة رموز لا زخرفة.** ملف واحد (`UI/Motion/Motion.cs`) يملك كل المدَد ومنحنيات التهدئة؛ ولا
  تُحرَّك إلا خصائص `RenderTransform` و`Opacity` (والاستثناء الوحيد إعادة تحجيم الوسائط في ⁦180⁩ مللي
  ثانية)، ولا شيء يستقصي دوريًّا، ومع إيقاف **الإعدادات ← عام ← الحركات** — أو إيقاف تأثيرات الحركة في
  Windows — تصبح كل الانتقالات فورية بلا كلفة في الخمول.
- **لا شيء يُستبدل.** تذهب النتائج إلى المجلد الذي حُلّت إليه الإفلاتة (قواعد ⁦2.2.3⁩) أو بجانب المصدر،
  والنتيجة الثانية تصبح `<الاسم> (2).<الامتداد>`.

#### الأداء والجودة

- تفشّل بوابة الحركة في `--self-test` الاختبار إذا تجاوز الإطار p95 في خيط الواجهة **⁦16.7⁩ مللي ثانية**،
  أو تجاوز خمول المعالج **⁦5⁩٪**، أو احتفظت أسطح الحركة بأكثر من **⁦12⁩ ميغابايت** بعد الإغلاق. وفي
  التشغيل المسجّل على رأس الدمج: p95 للواجهة **⁦0.02–0.07⁩ مللي ثانية**، وخمول المعالج **⁦0.39⁩٪**،
  والاحتفاظ **⁦0.0⁩ ميغابايت** (`docs/PERF.md`).
- يقيّد ضغط الفيديو متوسط مروره المزدوج بـ`maxrate`/`bufsize` ليبلغ الحجم المستهدف بأمانة، والتشغيل الملغى
  ينظّف ملفاته المؤقتة دون المساس بالمصدر.
- يحمل جدولا النصوص **⁦1,094⁩ مفتاحًا لكل منهما** بمجموعات مفاتيح وعناصر بديلة متطابقة، تفرضها اختبارات
  التوطين في CI؛ ويعرض الاختبار الذاتي التطبيق بالإنجليزية والعربية بالوضعين الفاتح والداكن.

#### ليس في هذا الإصدار

- **لا كشف وجوه في حجب الصورة.** المخرَج يشمل المناطق اليدوية وكشف النص بالتعرف الضوئي؛ أما كاشف الوجوه
  فيحتاج إمّا إسقاط Windows SDK الذي لا تحمله هذه النسخة أو حزمة خارجية جديدة.
- **قراءة رمز QR للقراءة فقط**، ولا يُفتح من الرمز إلا `http` و`https`.
- **قفل الملفات إصدار تجريبي** حتى يعتمده المالك نهائيًّا.
- الملفات التنفيذية ما زالت غير موقّعة بـAuthenticode (قد يسأل SmartScreen مرة واحدة)؛ أما التحديثات
  التلقائية فموقّعة منفصلًا بـ⁦ECDSA P-256⁩.
- جدولا النصوص مكتملان ومفروضان (⁦1,094⁩ مفتاحًا لكل منهما، بمجموعات مفاتيح وعناصر بديلة متطابقة،
  واختبارات تطابق في CI)، والاختبار الذاتي يعرض النسخة المُسلَّمة. أما بوابة عرض كل نافذة بالعربية
  والإنجليزية بدقة ⁦100⁩٪ و⁦150⁩٪ و⁦200⁩٪ فهي قيد الإنجاز في `audit/selftest`؛ وإلى أن تصبح خضراء لا
  يُدَّعى أن كل نافذة عُرضت باللغتين.

## 2.3.0

### English

- **Crop tool (first floating tool).** With a single image selected, the Tools wheel (Ctrl) now offers
  **Crop**; it opens a floating tool window instead of starting a job. Drag the rectangle by its corner or
  edge handles, see the area outside it dimmed, pick an aspect ratio (Free, 1:1, 16:9, 9:16, 4:3, 3:4) and
  adjust the width/height boxes in original-image pixels. Reset restores the whole image and Free.
  **Apply** writes `<name> Cropped.<ext>` next to the source — or into the folder the drop resolved — never
  overwrites the original, keeps the format and applies the EXIF orientation first.
- **A reusable floating-tool framework.** Tools declare their own chrome and preview bounds, get a
  borderless rounded card centred on the monitor under the cursor, draggable by its title, closed with Esc
  or the round X, with a Reset / primary-action bar. Each tool lives in its own file and registers itself,
  so adding the next tool never edits a shared file.
- **Tool windows adapt to the media.** The window opens at the tool's default size while the file is probed
  asynchronously (images decode a downsampled preview of at most 2048 px on the long side, with a spinner
  until it is ready), then animates in ~130 ms to the size that fits the media's aspect ratio inside the
  tool's preview bounds — portrait is tall and narrow, landscape wide and short, square balanced, and
  ultra-wide/ultra-tall are clamped so the controls stay usable. The result is clamped to 95% of the
  work-area width and 90% of its height of the monitor under the cursor, DPI aware; on a small screen the
  preview shrinks, never the controls. Tiny media is never enlarged beyond 200%. The rules are pure
  functions in `ToolWindowSizer`, unit tested for portrait, landscape, square, ultra-wide, ultra-tall,
  tiny, huge and a 1366x768 screen at 125%.

### العربية

- **أداة القصّ (أول أداة عائمة).** عند تحديد صورة واحدة تعرض عجلة الأدوات (Ctrl) خيار **قصّ**، فيُفتح إطار
  أداة عائم بدل بدء مهمة. اسحب المستطيل من مقابض الزوايا والحواف، والمنطقة خارجه معتمة، واختر نسبة الأبعاد
  (حر، 1:1، 16:9، 9:16، 4:3، 3:4) واضبط مربعَي العرض والارتفاع ببكسلات الصورة الأصلية. تعيد «إعادة ضبط»
  الصورة كاملة مع «حر». يكتب **تطبيق** ملفًا باسم `<الاسم> مقصوصة.<الامتداد>` بجانب المصدر أو في المجلد
  الذي حُلّت إليه الإفلاتة، دون استبدال الأصل، مع الحفاظ على الصيغة وتطبيق اتجاه EXIF أولًا.
- **إطار أدوات عائم قابل لإعادة الاستخدام.** تعلن كل أداة هيكلها وحدود المعاينة، ويظهر إطار مستدير بلا حدود
  في وسط الشاشة تحت المؤشر، يُسحب من شريطه ويُغلق بـEsc أو بزر X الدائري، مع شريط «إعادة ضبط» وزر الإجراء
  الأساسي. كل أداة في ملفها وتُسجّل نفسها، فلا تحتاج الأداة التالية إلى تعديل ملف مشترك.
- **تتكيّف نوافذ الأدوات مع الوسائط.** تُفتح النافذة بحجم الأداة الافتراضي بينما تُفحص الوسائط في الخلفية
  (الصور تُفكّ إلى معاينة مصغّرة بحد 2048 بكسل للضلع الأطول، مع مؤشر انتظار)، ثم تتحرك في نحو 130 مللي ثانية
  إلى الحجم الذي يناسب نسبة أبعاد الوسائط داخل حدود المعاينة: الطولي طويل وضيق، والعرضي عريض وقصير،
  والمربع متوازن، والشديد العرض أو الطول يُقيَّد لتبقى عناصر التحكم صالحة. يُقيَّد الناتج بـ95% من عرض مساحة
  العمل و90% من ارتفاعها لشاشة المؤشر مع مراعاة الـDPI، وعلى الشاشة الصغيرة تُصغَّر المعاينة لا عناصر التحكم،
  ولا تُكبَّر الوسائط الصغيرة أكثر من 200%. القواعد دوال نقية في `ToolWindowSizer` ومغطاة باختبارات
  للطولي والعرضي والمربع والشديد عرضًا وطولًا والصغير والضخم وشاشة 1366x768 عند 125%.

## 2.2.3

### English

- **The folder you drag onto is honoured in tabbed File Explorer.** Windows 11 lists every tab of one
  Explorer window as a separate shell window that shares the same top-level handle, so 2.2.2 could pick a
  background tab or no folder at all and save the result next to the source file. The tab window under the
  cursor is now matched through `IShellBrowser.GetWindow`, and while a drag is in progress the last folder
  the cursor crossed is remembered and used when the cursor is back over the source (a folder seen before
  the drag started is never used). Only an existing, writable folder that differs from the source folder is
  used, and a short notification names the destination. Every detection and decision is written to the log
  and the last one appears in `--diagnose`.
- **The wheel says where the result will go, before you drop.** A label in the middle of the wheel shows
  "Save to: <folder>" with the full path as its tooltip, "Save next to the file" when no other folder was
  found, and a warning "Can't save here, will save next to the file" when the folder refuses a write. It
  updates while the drag continues, following the same remembered-folder rules as the drop itself.

### العربية

- **يُحترم المجلد الذي تسحب إليه في مستكشف الملفات ذي التبويبات.** يعرض ويندوز 11 كل تبويب في نافذة
  المستكشف كنافذة غلاف منفصلة تشترك في المقبض الأعلى نفسه، لذا كان 2.2.2 قد يختار تبويبًا غير مرئي أو لا
  يجد مجلدًا فيحفظ النتيجة بجانب الملف الأصلي. الآن يُطابَق مقبض التبويب تحت المؤشر عبر
  `IShellBrowser.GetWindow`، ويُتذكَّر آخر مجلد عبره المؤشر أثناء السحب ويُستخدم عند رجوع المؤشر إلى مجلد
  المصدر (ولا يُستخدم مجلد رُئي قبل بدء السحب). لا يُستخدم إلا مجلد موجود وقابل للكتابة ويختلف عن مجلد
  المصدر، ويسمّي إشعار قصير وجهة الحفظ. تُكتب كل عملية كشف وقرار في السجل ويظهر آخرها في `--diagnose`.
- **تعرض العجلة وجهة الحفظ قبل الإفلات.** يظهر في وسط العجلة «الحفظ في: <المجلد>» ويحمل المسار الكامل
  كتلميح، و«الحفظ بجانب الملف» عند عدم العثور على مجلد آخر، وتحذير «لا يمكن الحفظ هنا، سيُحفظ بجانب
  الملف» عندما يرفض المجلد الكتابة. يتحدّث النص أثناء استمرار السحب وفق قواعد المجلد المتذكَّر نفسها.

## 2.2.2

### English

- **Results land in the folder you drop them on.** When the wheel opens over a File Explorer window, the
  folder shown there becomes the output folder for that drop, so a file dragged from the desktop into a
  folder on `D:` keeps its result in that folder instead of next to the original. The folder must exist,
  be writable and differ from the source folder; otherwise DropWheel keeps its previous behaviour (next to
  the file, or the folder chosen in Settings), and a notification explains a refused folder.

### العربية

- **النتائج تُحفظ في المجلد الذي تُفلتها عليه.** عندما تُفتح العجلة فوق نافذة مستكشف الملفات يصبح المجلد
  المعروض فيها هو مجلد الإخراج لتلك العملية، فيبقى ناتج ملف يُسحب من سطح المكتب إلى مجلد على `D:` داخل ذلك
  المجلد بدلًا من جوار الملف الأصلي. يجب أن يكون المجلد موجودًا وقابلًا للكتابة ومختلفًا عن مجلد المصدر،
  وإلا يحتفظ DropWheel بسلوكه السابق (بجوار الملف أو المجلد المختار في الإعدادات)، ويوضّح إشعار سبب رفض
  المجلد.

## 2.2.1

### English

- **P0 fix — self-contained 2.2.0 could not load its UI strings.** 2.2.0 published the self-contained app
  with `PublishTrimmed`; trimming disabled reflection-based `System.Text.Json`. `Loc.Load` threw, every lookup
  fell back to its key, and the installed app showed `settings.title` and other key names. The Settings window
  could then lay out enormous key strings and appear black. `Loc` now parses the same tables with `JsonDocument`.
- **Trimming is removed from the shipped build.** `PublishTrimmed`, `TrimMode` and `_SuppressWpfTrimError` are
  gone from the csproj and the publish script: WPF is not supported with trimming, and other dialogs can break
  in ways a self-test does not cover. The installer and portable zip carry the full .NET/WPF runtime; setup
  size is reported, not capped.
- **Settings can never be a silent black window again.** If building the window throws, DropWheel logs the
  exception and shows a readable error card instead of an empty black window; a missing header image is
  non-fatal.
- **CI now renders the actual shipped build.** The build job publishes the same untrimmed, self-contained
  win-x64 output the installer packages, runs `--self-test` on it, asserts that both string tables loaded
  (300+ keys) and uploads the log/screenshots. The real update banner is rendered too. It also rejects new
  generic reflection-based `JsonSerializer` calls.
- `--diagnose` reports the loaded string count and the resolved Settings title.
- Version 2.2.1; the download page and installer use the new version.

### العربية

- **إصلاح عاجل (P0): نسخة 2.2.0 المكتفية ذاتيًّا لم تكن تحمّل نصوص الواجهة.** كان 2.2.0 ينشر التطبيق
  المكتفي ذاتيًّا مع `PublishTrimmed`، والقصّ عطّل `System.Text.Json` المعتمد على الانعكاس. فشل `Loc.Load`
  وظهر بدل كل نص اسمُ مفتاحه، مثل `settings.title`، وقد تؤدي المفاتيح الطويلة إلى نافذة إعدادات سوداء.
  أصبح `Loc` يقرأ الجداول نفسها بـ `JsonDocument`.
- **أُزيل القصّ من النسخة المُسلَّمة.** حُذفت `PublishTrimmed` و`TrimMode` و`_SuppressWpfTrimError` من ملف
  المشروع وسكربت النشر: WPF غير مدعوم مع القصّ، وقد تتعطّل نوافذ أخرى بطرق لا يغطيها الاختبار الذاتي.
  يحمل المثبّت والأرشيف وقت تشغيل .NET/WPF كاملًا، ويُذكر حجم المثبّت بلا فرض حدّ.
- **لن تظهر نافذة إعدادات سوداء صامتة مرة أخرى.** إذا فشل بناء النافذة يسجّل DropWheel الاستثناء ويعرض
  بطاقة خطأ مقروءة بدل نافذة سوداء فارغة، وفشل صورة العنوان لم يعد قاتلًا.
- **CI يعرض النسخة المُسلَّمة فعليًّا:** ينشر مهمة البناء نفس مخرجات win-x64 المكتفية ذاتيًّا غير المقصوصة
  التي يحزمها المثبّت، ويشغّل عليها `--self-test`، ويتأكد من تحميل جدولي النصوص (أكثر من 300 مفتاح) ويرفع
  السجل ولقطات الشاشة. ويُعرض شريط التحديث الحقيقي أيضًا، ويرفض CI أي استدعاء عام جديد لـ `JsonSerializer`
  قائم على الانعكاس.
- يذكر `--diagnose` عدد النصوص المحمَّلة وعنوان نافذة الإعدادات.
- الإصدار 2.2.1، وصفحة التنزيل والمثبّت يستخدمان الإصدار الجديد.

## 2.2.0

### English

- **Signed auto-update.** DropWheel checks GitHub once a day (60 seconds after start, then every 24 hours) and
  when you click **Check for updates**. Nothing downloads until you click **Update**. Every download is checked
  twice: SHA-256 (the GitHub asset digest and `SHA256SUMS.txt`) and an ECDSA P-256 signature against the public
  key built into the app. If either check fails the files are deleted and DropWheel says **“Update could not be
  verified”** — an unverified file is never run.
- **Setup no longer needs .NET.** The installer is self-contained: no runtime detection, no download, no
  administrator permission for the default install. Choose **Install for me only** (no admin) or **Install for
  all users**. The wizard speaks English and Arabic (right-to-left).
- **Two setup tasks, both on by default:** *Start with Windows* and *Watch mouse and keyboard*. A Start-menu
  shortcut is always created; the desktop shortcut is optional.
- **Upgrades keep your choices.** The same settings file is reused, the app is closed during the update, and
  the uninstaller removes the startup entry, the Explorer menu, the Send-to shortcut and the program files —
  but keeps your settings unless you tick **Remove my settings**.
- **Settings reorganized into six tabs:** General · Wheels · Quality · Integration · Updates · About. Every
  switch appears exactly once.
- **Update UI:** a tray item, a Windows notification, and a violet banner in Settings with **Update now** and
  **What's new**.
- **Download page** at <https://faisaldhd.github.io/dropwheel/> in English and Arabic, with the current version
  and SHA-256.

### العربية

- **تحديث تلقائي موقَّع.** يتحقّق DropWheel من GitHub مرة كل يوم (بعد 60 ثانية من التشغيل ثم كل 24 ساعة) وعند
  الضغط على **التحقّق من التحديثات**. لا يُنزَّل أي شيء قبل أن تضغط **تحديث الآن**. ويُفحص كل تنزيل مرتين:
  بصمة SHA-256 (حقل digest في GitHub وملف `SHA256SUMS.txt`) وتوقيع ECDSA P-256 بالمفتاح العام المضمَّن في
  التطبيق. إذا فشل أي فحص تُحذف الملفات ويظهر **«تعذّر التحقق من التحديث»** — ولا يُشغَّل أي ملف غير موثَّق أبدًا.
- **المثبِّت لم يعد يحتاج .NET.** أصبح مستقلًّا بالكامل: لا كشف عن وقت التشغيل ولا تنزيل ولا صلاحيات مدير
  للتثبيت الافتراضي. اختر **التثبيت لي وحدي** (دون مدير) أو **التثبيت لكل المستخدمين**. المعالج بالعربية
  والإنجليزية (من اليمين إلى اليسار).
- **مهمتان في المثبِّت، مُفعَّلتان افتراضيًّا:** *البدء مع Windows* و*مراقبة الفأرة ولوحة المفاتيح*. ويُنشأ
  اختصار في قائمة ابدأ دائمًا، واختصار سطح المكتب اختياري.
- **الترقية تحافظ على اختياراتك.** يُعاد استخدام ملف الإعدادات نفسه، ويُغلق التطبيق أثناء التحديث، ويزيل
  برنامج الإزالة إدخال بدء التشغيل وقائمة Explorer واختصار «إرسال إلى» وملفات البرنامج — لكنه يحفظ إعداداتك
  إلا إذا فعّلت **إزالة إعداداتي**.
- **الإعدادات في ستة تبويبات:** عام · العجلات · الجودة · التكامل · التحديثات · حول. وكل مفتاح يظهر مرة واحدة فقط.
- **واجهة التحديث:** عنصر في علبة النظام وإشعار من Windows وشريط بنفسجي في الإعدادات مع **تحديث الآن**
  و**ما الجديد**.
- **صفحة تنزيل** على <https://faisaldhd.github.io/dropwheel/> بالعربية والإنجليزية، تعرض الإصدار الحالي
  وبصمة SHA-256.

## 2.1.1

- **Taskbar icon (R-29):** the Settings and Wheel editor windows forced a 16 px `BitmapImage` as their
  window icon, so the taskbar showed a 16 px image in a 24-32 px slot. Those overrides are gone; WPF now
  uses the executable's icon, which carries all nine frames (16-256 px), so Windows picks the right size
  at 100 % and 150 % scaling.
- **Dark title bar (R-30):** the title bar of the Settings and Wheel editor windows is now dark when the
  dark theme is active, via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`, and it follows a live
  theme switch without a restart.
## 2.1.0

Company-safe release.

- **Keyboard and mouse monitoring is now OFF by default.** With it off, DropWheel installs no global hooks
  at all; the wheel opens from the tray menu, the Explorer context menu or by dropping files. Turn it on in
  Settings > Company mode (it applies immediately), or pass `--no-hooks`.
- **Helper-tool downloads are now OFF by default.** DropWheel only uses FFmpeg/Pandoc found on PATH, installed
  by winget, or chosen by the user. Tool errors now name the tool, the folder and the OS error instead of
  blaming the output folder.
- **IT policy:** `HKLM\SOFTWARE\Policies\DropWheel\DisableHooks=1` and `DisableDownloads=1` lock the two
  switches machine-wide.
- Portable zip (self-contained, no admin, no registry) and a tag-triggered GitHub release.
- New `SECURITY.md` and `--diagnose` report for security reviews; low-integrity start is detected and refused
  with an explanation.
- Crash report file + fallback log locations, Arabic and English UI, brand icons.


DropWheel is the independent continuation of the MIT-licensed **FileAction** project. This is the first release where
nothing points back upstream: no update checks, no GitHub links, no automatic merges.

## Removed

- **Auto-update, completely**: `Updates.cs`, `Updater.cs`, the startup check, the tray item, the notifications, the
  `--updated` argument and its installer run entry, the "check for updates" setting, and the matching tests.
- **Every link to the original project**: README sections (Releases, GitHub Actions, self-update), `.github/workflows`,
  and the `origin` git remote. `grep -i github src/` → 0 hits; the only network calls left are the on-demand FFmpeg
  (gyan.dev) and Pandoc (api.github.com, the publisher's release API) downloads, both SHA-256 verified.
- **Czech UI**: `README.cs.md`, the Czech screenshots, the installer's Czech language and the Czech half of every string.
  `Loc.T()` now always returns English. An Arabic UI can be added the same way later.
- **Windows Forms**: the tray icon is now `Shell_NotifyIcon` + a WPF `ContextMenu`, so `UseWindowsForms` is gone and
  ~19 MB of `System.Windows.Forms*.dll` no longer ship.

## Rebranded

- User-visible identity only: product, exe, windows, tray, installer and README are **DropWheel**. Internal
  project/namespace names stay FileAction.* by owner rule (renaming them adds risk without user value).
- Paths: `%APPDATA%\DropWheel` (settings), `%LOCALAPPDATA%\DropWheel` (tools), `Downloads\DropWheel` (inbox).
  A one-time migration moves the old `FileAction` folders and the old autostart value on first start.
- Registry: the autostart value, the Explorer context-menu verb, the single-instance mutex and pipe names, the AUMID.
- New icon and name everywhere (windows, tray, About, installer, Start menu); the installer uses a new `AppId`
  (`{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}`) so it can never collide with the original app.
- Licence: MIT text kept verbatim with `Copyright (c) 2026 Metamorfolog` (required) plus `Copyright (c) 2026 DropWheel`.

## Added since 1.4.0

- **Drop anywhere**: releasing the wheel over another folder saves the result there; over the source folder it stays
  beside the original.
- **Previews**: an image thumbnail in the image tool dialogs, a five-frame FFmpeg filmstrip plus duration in Trim and
  Speed.
- **PDF → XLSX (tables only)**, with a clear "No tables found in this PDF." failure instead of an empty workbook.
- **Violet Glass theme**: violet/lavender accent, new dark and light palettes, and the new brand icon.

## Sizes

| | Size | Files |
|---|---|---|
| Publish folder | 219.3 MB | 293 |
| `DropWheel-Setup-2.0.0.exe` | **63.9 MB** (66,994,074 bytes) | — |

`DebugType=none`, and `createdump.exe`, `Microsoft.DiaSymReader.Native.amd64.dll` and the duplicate `mscordaccore.dll`
are stripped from the release folder.

## Verification

- `dotnet test tests\FileAction.Tests`: **50 passed, 0 failed**.
- REA `inspect_managed_artifact` on `release\DropWheel\DropWheel.dll`:
  assembly **DropWheel 2.0.0.0**, .NET 10, 161 types / 968 methods,
  SHA-256 `ed378ca82490e60b939612029bbf0e522fc9cca1abd4c8c0f9c97f81b6c29c45`.
- REA `inspect_pe_resources` on `DropWheel-Setup-2.0.0.exe`: PE32, 66,967,800 bytes,
  SHA-256 `B7B9095A51AF2AC3802176D2D0DDCEE2E873768C7E885DD2A40C6008DC59AAB1`,
  `RT_GROUP_ICON` with nine images (16, 20, 24, 32, 40, 48, 64, 128, 256 px);
  version resource: ProductName/CompanyName **DropWheel**, ProductVersion **2.0.0**.

## Not done / known limits

- The simplified 16 px tray icon variant and the light/dark tray switching are not implemented yet; the tray uses the
  full application icon.
- `--self-test` screenshots for `docs\screenshots\` were not re-generated in this environment.
- Acrylic/mica on the Settings window (Windows 11) is not implemented.
- PDF → XLSX depends on a LibreOffice build that can import PDF tables; the LibreOffice on the build machine cannot, so
  the operation fails cleanly there.

## Brand update (Violet solid, no glass)

- Accent is now #8224D4 with #B48AE0 outlines; the accent gradient is a solid fill everywhere (selected slice,
  active tab, progress bars), so no glass/frosted look remains.
- Dark palette slices #241A35, page #181220; glow reduced to BlurRadius 14 (0.45 dark / 0.3 light); dark is the
  default theme.
- Tray icons: new flat 8-slice wheel, DropWheel-tray-dark.ico / DropWheel-tray-light.ico, chosen at startup from
  HKCU\...\Themes\Personalize\SystemUsesLightTheme (live switching not implemented yet).
- Brand icon (randing\DropWheel.ico, 16-256) is embedded in the exe and the installer.
