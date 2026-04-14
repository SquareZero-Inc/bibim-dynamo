# Common Revit API Patterns — RAG Reference

> 이 문서는 BIBIM AI가 자주 잘못 생성하는 Revit API 패턴을 정리한 것입니다.
> RAG 인덱싱 대상이며, 코드 생성 시 올바른 패턴을 참조하도록 합니다.

---

## 1. Schedule/View에서 요소 가져오기

### ✅ 올바른 패턴
```python
# Schedule(또는 View)에 표시된 요소를 가져오는 가장 간단하고 정확한 방법
collector = FilteredElementCollector(doc, schedule.Id)
elements = collector.ToElements()
```

- `FilteredElementCollector(doc, viewId)` 는 해당 뷰/스케줄에 적용된 필터, 위상(Phase) 조건을 자동으로 반영합니다.
- 스케줄의 경우 `schedule.Id`를 viewId로 전달하면 됩니다.

### ❌ 잘못된 패턴
```python
# 존재하지 않는 메서드 — Revit API에 없음
element_ids = schedule.GetElementIdsFromBody()  # AttributeError 발생

# 수동 필터 매칭 — 불필요하게 복잡하고 오류 발생 가능
for i in range(schedule.Definition.GetFilterCount()):
    sf = schedule.Definition.GetFilter(i)
    # ... 100줄 이상의 수동 비교 로직
```

### 근거
- Brian Nene 케이스 (2026-03-10): AI가 `GetElementIdsFromBody()` 를 환각하여 호출 후, fallback으로 100줄 이상의 수동 필터 매칭 코드를 생성함.

---

## 2. DWG/DXF Export 설정 가져오기

### ✅ 올바른 패턴
```python
# 방법 1: FilteredElementCollector 사용
settings = FilteredElementCollector(doc).OfClass(ExportDWGSettings).FirstElement()

# 방법 2: 활성 프리셋 가져오기 (단일 객체 반환)
active_settings = ExportDWGSettings.GetActivePredefinedSettings(doc)
# active_settings는 단일 ExportDWGSettings 객체임 — 리스트가 아님
```

### ❌ 잘못된 패턴
```python
# GetActivePredefinedSettings()는 단일 객체를 반환 — iterable이 아님
for setting in ExportDWGSettings.GetActivePredefinedSettings(doc):  # TypeError 발생
    pass
```

### 근거
- 성소진 케이스 (2026-03-10): AI가 `GetActivePredefinedSettings(doc)` 반환값을 리스트로 가정하고 for 루프를 돌려 `TypeError: iteration over non-sequence` 발생.

---

## 3. Document.Export() 사용법

### ✅ 올바른 패턴
```python
# Export는 읽기 전용 작업 — Transaction 불필요
options = DWGExportOptions()
view_ids = List[ElementId]()
view_ids.Add(view.Id)
doc.Export(folder_path, file_name, view_ids, options)
```

### ❌ 잘못된 패턴
```python
# Transaction으로 감싸면 실패함
TransactionManager.Instance.EnsureInTransaction(doc)
doc.Export(folder_path, file_name, view_ids, options)  # 에러 발생
TransactionManager.Instance.TransactionTaskDone()
```

---

## 4. FilteredElementCollector 일반 규칙

| 용도 | 올바른 호출 |
|------|------------|
| 문서 전체 요소 | `FilteredElementCollector(doc)` |
| 특정 뷰/스케줄의 요소 | `FilteredElementCollector(doc, viewId)` |
| 특정 클래스의 요소 | `.OfClass(typeof(ClassName))` |
| 특정 카테고리의 요소 | `.OfCategory(BuiltInCategory.OST_xxx)` |

### 핵심 원칙
- Collector 오버로드가 존재하면 수동 필터링 로직을 작성하지 말 것
- `FilteredElementCollector(doc, viewId)`는 뷰 필터 + 위상을 자동 반영함
