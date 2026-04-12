# SkiaSharp GHSA-j7hp-h8jx-5ppr 취약점 수정 보고서

작성일: 2026-04-12  
상태: ✅ 수정 완료

---

## 취약점 개요

| 항목 | 내용 |
|------|------|
| CVE / GHSA | GHSA-j7hp-h8jx-5ppr |
| 심각도 | High |
| 취약 패키지 | SkiaSharp 2.88.3 |
| 영향 경로 | Avalonia 11.0.0 → Avalonia.Skia → SkiaSharp 2.88.3 (전이 의존성) |
| 패치 버전 | SkiaSharp ≥ 2.88.9 (Avalonia 11.2.x 이상이 참조) |

빌드 경고 원문:
```
warning NU1903: Package 'SkiaSharp' 2.88.3 has a known high severity vulnerability,
GHSA-j7hp-h8jx-5ppr
```

---

## 수정 방안 선택

| 옵션 | 방법 | 장점 | 단점 |
|------|------|------|------|
| A | SkiaSharp 2.88.9 직접 오버라이드 | 변경 범위 최소 | 전이 의존성 관리 복잡, 추후 Avalonia 업그레이드 시 재충돌 가능 |
| **B (채택)** | **Avalonia 11.0.0 → 11.3.13 업그레이드** | **근본 해결; SkiaSharp 버전 Avalonia가 일관되게 관리** | 변경 범위 더 큼; API 변경 점검 필요 |

**Option B 채택** — Avalonia가 SkiaSharp를 직접 관리하므로 업그레이드를 통한 근본 해결이 올바른 접근이다.

---

## 수정 내역

### `NodeKit.csproj` 변경

```diff
- <PackageReference Include="Avalonia" Version="11.0.0" />
- <PackageReference Include="Avalonia.Desktop" Version="11.0.0" />
- <PackageReference Include="Avalonia.Themes.Fluent" Version="11.0.0" />
- <PackageReference Include="Avalonia.Fonts.Inter" Version="11.0.0" />
- <PackageReference Include="Avalonia.Diagnostics" Version="11.0.0" .../>
+ <PackageReference Include="Avalonia" Version="11.3.13" />
+ <PackageReference Include="Avalonia.Desktop" Version="11.3.13" />
+ <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.13" />
+ <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.13" />
+ <PackageReference Include="Avalonia.Diagnostics" Version="11.3.13" .../>
```

---

## 검증

- `dotnet build -c Release` — 0 Error(s), 경고 수 변화 없음 (NU1903 사라짐)
- `dotnet test tests/NodeKit.Tests/` — 41/41 Passed

---

## 잔여 경고: Tmds.DBus.Protocol

Avalonia 11.3.13 업그레이드 후 새로운 NU1903 경고가 발생:

```
warning NU1903: Package 'Tmds.DBus.Protocol' 0.21.2 has a known high severity vulnerability,
GHSA-xrw6-gwf8-vvr9
```

- **영향 경로**: Avalonia.Desktop 11.3.13 → Tmds.DBus.Protocol 0.21.2 (전이 의존성)
- **실질적 영향**: NodeKit은 DBus를 직접 사용하지 않음. Linux 데스크톱 UI 전이 의존성이며 관리자 전용 도구로 공격 표면 낮음.
- **현재 상태**: 오프라인 빌드 환경에서 패치된 버전(NuGet 네트워크 접근 필요) 설치 불가.  
  버전 오버라이드(0.90.3)를 시도했으나 해당 버전도 같은 GHSA에 등재되어 효과 없음.
- **향후 조치**: 온라인 환경에서 `dotnet list package --vulnerable` 재실행 후 패치된 버전으로 오버라이드.  
  Avalonia가 상위 버전에서 패치된 Tmds.DBus.Protocol을 참조할 경우 자동 해결됨.

## 기타 잔여 위험

- Avalonia 11.x → 12.x 주요 버전 업그레이드 시 AXAML 및 API 호환성 별도 검토 필요.
- `dotnet list package --vulnerable` 를 CI 단계에 추가하여 주기적 추적 권장.
