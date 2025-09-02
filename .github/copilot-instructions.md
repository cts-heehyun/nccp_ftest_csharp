# Copilot Instructions (C# 프로젝트 가이드)

이 문서는 GitHub Copilot이 C# 코드 작성 시 따라야 할 기본 지침을 정의한다.  
목표는 일관된 코드 스타일 유지와 가독성 높은 코드 생성을 돕는 것이다.

---

## 1. 코드 스타일
- **네이밍 규칙**
  - 클래스, 메서드: PascalCase (예: `MyClass`, `CalculateValue`)
  - 지역 변수, 매개변수: camelCase (예: `userName`, `maxCount`)
  - 상수: UPPER_CASE (예: `MAX_VALUE`)
- **중괄호 스타일**
  - K&R 스타일 사용  
    ```csharp
    if (condition) {
        // code
    } else {
        // code
    }
    ```
- **들여쓰기**
  - 스페이스 4칸 사용
- **using**
  - 불필요한 using 제거

---

## 2. 주석 작성
- 메서드, 클래스, 인터페이스에는 **XML 주석** 사용  
  ```csharp
  /// <summary>
  /// 두 수를 더한다.
  /// </summary>
  /// <param name="a">첫 번째 정수</param>
  /// <param name="b">두 번째 정수</param>
  /// <returns>합계</returns>
  public int Add(int a, int b) {
      return a + b;
  }
  ```
- 중요한 로직은 한글 주석으로 설명
- 불필요한 주석은 작성하지 않는다

---

## 3. 코드 작성 원칙
- SOLID 원칙을 지향
- 비동기 메서드: async/await 사용
- 예외 처리: 구체적 예외 사용
- 최신 C# 문법 사용 (var, 패턴 매칭, switch expression 등)
 
---

## 4. 테스트 코드
- 단위 테스트는 xUnit 또는 NUnit 기반 작성
- 메서드명은 설명형 네이밍 사용
	예: CalculateSum_WhenInputIsValid_ReturnsCorrectValue

---

## 5. Copilot 코드 생성 지침
- 코드를 생성할 때 지침을 준수
- 코드 블록 하단에 검토 결과 주석 추가
	```csharp
	// [Copilot 검토 결과]
	// - 네이밍 규칙: OK
	// - XML 주석: 누락됨 → 보완 필요
	// - 예외 처리: OK
	```

---

## 6. 코드 생성 후 자동 빌드 및 분석
Copilot이 생성한 코드는 다음 절차를 따라야 함:
- 코드 생성 후 검토
  - 네이밍, 주석, 최신 문법 확인
- 커밋/PR 제출 시 자동 빌드 및 테스트
- 정적 분석 도구 실행
  - StyleCop.Analyzers: 네이밍, 주석, 코드 스타일
  - dotnet format: 포맷 체크
  - SonarQube (선택): 품질, 보안, 복잡도 분석
- 검토 결과 보고
  - PR 코멘트 또는 GitHub Checks
  - 실패 시 병합 차단, 성공 시 병합 가능
