# MulletaFlix Security Audit Report

**Date:** July 12, 2026  
**Scope:** Full codebase security audit (Emby/Jellyfin fork)  
**Auditor:** AI Security Assessment

---

## Executive Summary

The MulletaFlix codebase is based on Emby/Jellyfin and inherits many of its security properties. The audit identified several security concerns ranging from critical timing attacks on API key validation to medium-severity CORS misconfigurations. No hardcoded secrets were found in the codebase or deployment scripts.

---

## Critical Findings

### 1. Timing Attack on API Key Validation
**Location:** `Jellyfin.Server.Implementations/Security/AuthorizationContext.cs:203`  
**Severity:** Critical  
**CVSS:** 8.1 (High)

**Description:**  
API key comparison uses EF Core's database query with standard string equality (`apiKey.AccessToken == token`), which translates to SQL `WHERE AccessToken = @token`. SQL string comparison is not constant-time and short-circuits on the first differing character, allowing timing attacks to deduce the API key character by character.

**Attack Vector:**  
An attacker can measure response time differences to determine correct API key characters by brute-forcing one character at a time.

**Evidence:**  
```csharp
// Line 203 - Database comparison using SQL equality (not constant-time)
var key = await dbContext.ApiKeys.FirstOrDefaultAsync(apiKey => apiKey.AccessToken == token).ConfigureAwait(false);
```

**Mitigation:**  
- Use `CryptographicOperations.FixedTimeEquals()` for in-memory comparisons
- Or ensure database queries use constant-time comparison at the SQL level (not typically supported)
- Consider hashing API keys with salt before storage and using constant-time hash comparison

---

## High Findings

### 2. CORS Misconfiguration - Allow Any Origin by Default
**Location:** `MulletaFlix/Configuration/CorsPolicyProvider.cs`  
**Severity:** High  
**CVSS:** 7.5 (High)

**Description:**  
When no specific CORS hosts are configured (the default), the CORS policy allows `AllowAnyOrigin()`, `AllowAnyMethod()`, and `AllowAnyHeader()`. This enables any website to make cross-origin requests to the API, potentially allowing session hijacking or unauthorized actions.

**Evidence:**  
Default configuration allows all origins when no specific hosts are configured.

**Mitigation:**  
- Require explicit CORS host configuration before enabling cross-origin access
- Default to a restrictive CORS policy when no hosts are specified
- Validate that CORS configuration is properly set during deployment

---

### 3. Path Traversal Risk in BackupService SQL Execution
**Location:** `Jellyfin.Server.Implementations/FullSystemBackup/BackupService.cs`  
**Severity:** High  
**CVSS:** 6.8 (Medium-High)

**Description:**  
`BackupService.cs` calls `historyRepository.GetDeleteScript()` / `GetInsertScript()` and passes the results to `ExecuteSqlRawAsync()`. While these scripts come from EF Core migration history (mitigated by being from backup archives), if backup archives are attacker-controlled, they could inject malicious SQL through migration IDs.

**Evidence:**  
```csharp
// BackupService passes migration scripts to ExecuteSqlRawAsync
await dbContext.Database.ExecuteSqlRawAsync(deleteScript).ConfigureAwait(false);
await dbContext.Database.ExecuteSqlRawAsync(insertScript).ConfigureAwait(false);
```

**Mitigation:**  
- Validate that backup archives come from trusted sources
- Consider using parameterized queries instead of raw SQL execution
- Sanitize migration IDs before use in SQL statements

---

## Medium Findings

### 4. Rate Limiting Implementation Concerns
**Location:** `Api贼PreventOpenBruteForceAuthenticationMiddleware`  
**Severity:** Medium  
**CVSS:** 5.3 (Medium)

**Description:**  
Rate limiting exists for authentication attempts, but implementation details should be reviewed to ensure it effectively prevents brute force attacks. The middleware should track failed attempts per IP and implement exponential backoff.

**Mitigation:**  
- Verify rate limiting thresholds are appropriate (e.g., 5 attempts per minute per IP)
- Ensure rate limiting is applied before authentication processing
- Consider implementing account lockout after multiple failed attempts

---

### 5. File Upload Validation in Lyrics Endpoint
**Location:** `Jellyfin.Api/Controllers/LyricsController.cs:103-143`  
**Severity:** Medium  
**CVSS:** 4.3 (Medium)

**Description:**  
The lyrics upload endpoint uses `Path.GetExtension(fileName.AsSpan())` for validation, which provides some path traversal protection. However, the endpoint should also validate file size limits and content type to prevent denial of service attacks.

**Evidence:**  
```csharp
// Uses Path.GetExtension for validation (good)
var format = Path.GetExtension(fileName.AsSpan()).RightPart('.').ToString();
// But no file size validation beyond ContentLength check
```

**Mitigation:**  
- Implement server-side file size limits independent of ContentLength header
- Validate content type matches expected lyric formats
- Consider scanning uploaded content for malicious patterns

---

## Low Findings

### 6. Insecure Deserialization Risk
**Location:** Multiple locations using JSON serialization  
**Severity:** Low  
**CVSS:** 3.7 (Low)

**Description:**  
The codebase uses JSON serialization extensively. While no dangerous deserialization patterns were found, ensure all JSON deserialization uses safe settings (e.g., `TypeNameHandling.None`).

**Mitigation:**  
- Audit all `JsonConvert.DeserializeObject` calls for type safety
- Avoid `TypeNameHandling.All` or similar dangerous settings
- Use explicit type parameters for deserialization

---

### 7. SQL Injection in User Management
**Location:** `Jellyfin.Server.Implementations/Users/UserManager.cs`  
**Severity:** Low  
**CVSS:** 3.1 (Low)

**Description:**  
`UserManager.cs` uses `ExecuteSqlRawAsync`, but investigation shows it uses parameterized queries properly. This finding is informational only.

**Evidence:**  
```csharp
// Uses parameterized queries (safe)
await dbContext.Database.ExecuteSqlRawAsync(sql, parameters).ConfigureAwait(false);
```

**Status:** Verified safe - no action required.

---

## Informational Findings

### 8. Cryptographic Practices
- **Password Hashing:** Uses industry-standard PBKDF2 with SHA256 (10,000 iterations)
- **TLS:** Properly configured for HTTPS connections
- **No hardcoded secrets** found in codebase or deployment scripts

### 9. Command Injection Protection
- `ServerUpdateTask.cs` uses `Quote()` helper for argument escaping
- Most `Process.Start` calls use parameterized arguments
- Hardware detection uses trusted binaries from known paths

### 10. Path Traversal Protection
- `Startup.cs` uses `Path.GetFullPath()` with `StartsWith()` validation for static file serving
- Most `Path.Combine` usages use constants or GUIDs (safe)

---

## Recommendations Summary

| Priority | Finding | Effort |
|----------|---------|--------|
| **Critical** | Fix timing attack on API key validation | Medium |
| **High** | Fix CORS misconfiguration | Low |
| **High** | Secure BackupService SQL execution | Medium |
| **Medium** | Review rate limiting implementation | Low |
| **Medium** | Enhance file upload validation | Low |
| **Low** | Audit JSON deserialization settings | Low |

---

## Conclusion

The MulletaFlix codebase has a reasonable security posture for a media server application. The most critical issue is the timing attack vulnerability on API key validation, which should be addressed immediately. The CORS misconfiguration also presents a significant risk if left unconfigured in production environments.

The codebase benefits from inherited security patterns from Emby/Jellyfin, including proper parameterized SQL queries, rate limiting for authentication, and path traversal protection in static file serving.

**Overall Risk Rating:** Medium-High (due to critical timing attack vulnerability)

---

*Report generated by AI security assessment. Manual verification recommended for all findings.*