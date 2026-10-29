# Bamboo30 update: orientation, persistence and pen margins

Prepared on 29 October 2026 for `gonleon59/Silicon-Tablet-Driver`, branch `silicon-preview`. This update contains all changes since published bamboo27 (`7a89ad56cf62e6afd7da22f3ab118d66afc7b2fe`), including local bamboo28 and bamboo29. The application version remains `0.6.7-bamboo30`; no new pressure or touch compatibility fix is implied by the documentation update.

**Tested on macOS 27.2 with a Mac mini M4 (Apple Silicon) and Wacom Bamboo CTH-661 over USB.** Minimum build target remains macOS 14; other OS versions and tablet models are not certified by these results.

## User-visible changes since bamboo27

1. **Correct landscape positioning.** Right-handed 0° and left-handed 180° modes use their own pen geometry. Old vertical cropping, scaling, near-user placement and screen-proportion lock no longer affect them. Vertical 270° mode retains the optional comfortable-area preset, or can use the complete oriented pen area.
2. **Save keeps orientation editable.** Preset metadata survives saving even with comfortable cropping disabled. Orientation and display controls no longer depend on that toggle. Successful Save, a rejected Save and Revert release the controls correctly.
3. **Preserve user choices.** Unrelated edits preserve manual area geometry. Explicit orientation/display changes or the standard-area action opt back into preset calculation. Existing manual output rectangles recover their monitor selection instead of automatically selecting every screen. Saved margins survive round trips. Existing custom margins are preserved; the new defaults do not overwrite every existing profile automatically.
4. **Standard pen margins.** Both landscape presets default to 4.5 mm on each side and 5 mm at top/bottom: a centered 207.48 × 127 mm pen area using the driver's 216.48 × 137 mm digitizer dimensions. Bamboo29 initially used 9/10 mm; bamboo30 halves these defaults. Vertical crop controls are unavailable in landscape; preview and captions show the standard margins.
5. **Numeric input and search.** Switching to landscape clears unfinished drafts only for the crop fields that become hidden. Unrelated invalid input remains invalid. Landscape search omits unavailable vertical crop controls.
6. **Buttons.** Applying a horizontal preset re-enables body buttons; the vertical preset disables the four body buttons. Pen buttons remain available.
7. **Regression coverage and CI.** New tests cover actual absolute-coordinate transforms, negative-coordinate screens, manual-area preservation, monitor selection, margin persistence, Save/Revert/editability and touch independence. GitHub Actions adds the production Swift settings-model tests and changes the package suffix to `-bamboo30`.

The earlier bamboo27 USB/HID snapshot and lost-connection fixes remain included; see [their report](BAMBOO_HOTPLUG_FIX.md). They are not new changes in this update. This update does not change permission prompts, process ownership or production gesture recognition.

## Touch area: hardware limit, not a new software crop

Wacom specifies **190 × 130 mm for touch** and **216.5 × 137 mm for pen** on CTH-661 ([Bamboo manual, page 75](https://cdn.wacom.com/u/productsupport/manuals/Bamboo2/User%27s%20Manual.pdf#page=75)). “Full touch area” means the whole hardware touch sensor, not the larger pen area or plastic surface. Its exact placement relative to every physical edge was not measured.

Software checks pass touches across the complete 0…2047 coordinate range, including all four corners, through the touch filter and absolute-output pipeline with pen clipping enabled. Edge movements are compared with equivalent center movements in both landscape orientations. These tests establish independence from pen mapping; they do not establish physical sensor boundaries or real macOS pointer behavior at every edge. Production touch parsing/gesture code is unchanged in bamboo28–30.

## Pressure: verified delivery and unresolved Curve behavior

During local hardware checks the user confirmed that pressure varies with force in the driver's input test. A separate AppKit receiving window recorded 814 events, 115 distinct pressure values and a range of 0…1, including tablet-point and tablet-point mouse-drag events. This confirms pressure delivery to that receiving application; it does not certify every application's interpretation of the synthetic tablet events.

In **Linearity Curve 7.1.1**, the user tested a **new Brush (B) stroke** and reported no pressure-dependent width. No pressure switch was found in the inspected Mac Brush Editor or general settings. The cause is not established; Curve support is not claimed fixed or universally unsupported. Adobe pressure/eraser support remains untested. Private diagnostic recordings and user documents are excluded from this source update.

## Validation and limits

| Check | Result |
| --- | --- |
| C# tests, Release, prepared source copy | 2,251 passed; 6 existing configuration skips; 0 failures |
| Production Swift settings model | 22 checks passed |
| Swift numeric input | 18 checks passed |
| Swift settings search | 12 checks passed |
| ARM64 packaging | `0.6.7-bamboo30`, bundle build 30; signature integrity verified |
| Physical pen positioning | User confirmed after bamboo28 orientation fix; later margins covered by software tests |
| Native settings | Right/left/vertical modes, save and continued editing checked locally on bamboo28; model and persistence tests rerun for bamboo30 |
| Pressure | Tablet and AppKit receiving tests passed; Curve Brush width test did not |
| Touch boundaries | Software independence passed; physical active area follows Wacom specifications, boundaries not measured |

Fresh verification of this prepared source was performed in a separate copy on macOS 27.2. It does not replace the installed driver or change user settings. The verification package is ad-hoc signed; it is not Apple-notarized or a Developer ID release. The separately installed development build uses its existing local identity. A new public identity or signature needs separate permission/migration checks.

Clean-account installation, the full sleep/wake/reboot/login sequence, repeated physical USB/dock reconnection, all advertised gestures in every application, and one unsaved-draft discard-and-quit branch remain unverified. Local results are separate from GitHub Actions: adding the workflow step is not evidence that it has run remotely.

## Кратко по-русски

Обновление собирает все изменения bamboo28–30 после публичной bamboo27: исправлены горизонтальная ориентация и блокировка её выбора после сохранения; сохраняются ручная область, мониторы и отступы. Стандартные отступы пера — **4,5 мм слева/справа и 5 мм сверху/снизу**. Сенсор касаний остаётся независимым от области пера и ограничен своей физической площадью **190 × 130 мм**.

**Протестировано на macOS 27.2, Mac mini M4 и CTH-661.** Успешно прошли 2251 C#-тест (6 прежних пропусков), 22 проверки модели Swift, 18 числового ввода и 12 поиска, сборка ARM64 и проверка подписи. Нажим доходит до отдельного приложения AppKit, но в проверке пользователя кисть Linearity Curve 7.1.1 не меняла толщину от нажима. Причина не установлена, исправление Curve в это обновление не входит.

Implementation, review and documentation include AI assistance. See [AGENTS.md](../AGENTS.md) and [the changelog](BAMBOO_CHANGELOG.md).
