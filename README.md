# Tiny Json
This is a non-trivial revision to the original [zanders3/json](https://github.com/zanders3/json) project by Alex Parker. I've incorporated notable additions from other forks, uncommitted PRs, and resolved various issues. TinyJson hasn't been that tiny in recent years, but my goal is to reorganize and enhance the project while remaining under 1000 lines in a single *.cs file output.

## TLDR:
The interface of this library is considered to be self-documenting and simple to understand, just go read the XML Documentation comments in the [TinyJsonSystemExtensions.cs](https://github.com/JD-Howard/TinyJson/blob/main/src/TinyJsonSystemExtensions.cs) file.

TinyJson is a great option if you:
- Don't want additional dependencies.
  - Supply chain attack surface reduction was definitively one of my motivations.
  - this is configured for NetStandard2.0 targeting, but if your importing this code directly into your project, then you may need to upgrade your CSPROJ LangVersion attribute to 8.
- Don't have complex JSON needs. TinyJson does have:
  - Parse operations that always ignore property/field casing.
  - Has fairly robust support for nullable & generic types.
  - Does support a limited set of attribute modifiers such as IgnoreDataMember & DataMember(name ="").
  - All serialized structs/objects require default constructors.
  - Does NOT support abstract or interface types, but will of course work with their concrete inheritors.
- Need to work with lots of small JSON chunks.
  - Meets or beats Newtonsoft performance for small items.
- Don't need to work with large JSON files.
  - Newtonsoft is 2x faster than TinyJson at parsing a fairly nested (many) small-object 8MB file.

You can find a 1-file merged CS output with no external dependencies in the Release folder. This folder also contains a DLL if your incapable of upgrading your CPROJ C# LangVersion attribute. I suggest using the CS file, doing a search/replace on `public static` for `internal static`, and just using it in isolation. I tend to stick this in my "shared" NetStandard DLL with internal/static and use `InternalsVisibleTo` to expose the functionality for my consumer projects.

**NOTE:** Given that I don't really want or need to dig into the lingering _performance at scale_ parsing issue, this project update is largely complete at this point. There will be additional updates, especially in the area of unit testing, but this should be fairly close to the functional, 1-file, works everywhere, json capabilities that I needed. **Absolutely interested in pull requests if you want to help improve the functionality or expand unit tests.**


## Change Log
For me, the intent of this project is to internalize these files into another project that needs basic JSON support. So, this change log will not focus on versions and simply document what each of my commits accomplished.

### 2024-08-04 Commit [7b16d4b](https://github.com/JD-Howard/TinyJson/commit/7b16d4bd41c884a86de554adec19d0c196e0106e)
#### Project File Changes:
- Changed the json project to target NetStandard2.0 so it will largely work everywhere.
- Changed the json project CSPROJ to use the latest C# language features.
- Changed the test project to target net8 so I could use the latest MSTest nuget packages.
- Added `.editorconfig` to preserve the original unix line endings.
- Added `nuget.config` to keep the packages I do use in testing project localized to the solution folder.
- Updated the `.gitignore` to be more generalized and deal with my usage of Rider.

#### JSONParser.cs Changes:
- General cleanup to abstract some of the complexity into methods.
  - Also attempted some micro optimizations, see note below for real problem.
- Imported the concept of ignoring enum case from the [RealmJoin](https://github.com/realmjoin/json) fork
- Added more explicit support for nullable primitives.
- Handling the primitive parsing in a much more consistent way.
- Identified that custom generic types are non-functional and need more work.
- Removed the restriction to Dictionary<string,T> and you can pretty much use any primitive for a key now. I have not tested Enum, but it should work. Probably not capable of object keys...
- The concrete implementations of Abstract/Generic classes should work in many scenarios. 
- More built-in Struct types are now handled explicitly to reduce json size.
- Migrated the DefaultValueAttribute handler from the [SpecFlow](https://github.com/SpecFlowOSS/SpecFlow.Internal.Json) fork.

**NOTE:** 
It has become clear that this will never perform at the level it should while using a StringBuilder. In order to fix this, a single-pass char[] strategy closer to the way SimpleJson parses would need to be deployed. I will continue to get this where I need it for my immediate netstandard needs, but *it would be ill-advised to use TinyJson on truly large json files;* such as +50MB. ~~Also, I may in fact fork/migrate/update SimpleJson too given this large implementation limitation.~~ Update: [SimpleJson](https://github.com/facebook-csharp-sdk/simple-json) is fast, but it is doing very strange/non-standard serialization and not a viable option...


#### JSONWriter.cs Changes:
- Changed the DateTime to JSON format to match ISO 8601; which preserves timezone information.
- Added a TimeSpan handler so it will stop creating excessively large representations.

**NOTE:**
This writer tends to entirely omit null values rather than including them in the output JSON. I don't think this is a big problem, it makes smaller json, but it is a little odd to see {} representing an entire object, and this may be an undesirable for some users or even an issue in some situations.

#### Test Changes:
- I copied/modified a number of Date related tests from the [SpecFlow](https://github.com/SpecFlowOSS/SpecFlow.Internal.Json) fork of TinyJson
  - Previous date tests were just removed, leaning on SpecFlow versions.
- Made sure all the original tests continued to pass.
- Did some comparison testing with Newtonsoft so I could get an understanding of TinyJson performance characteristics
  - Its generally better than Newtonsoft for small JSON.
  - The "TestParser.PerformanceTest()" aka stress test is 1:3 slower than Newtonsoft
  - See note above for why, but if you are only working with small JSON, this is faster.
- Added a complex scenario and it successfully parsed it; TODO verify ToJson version works.
  - `class GenericComplexImp<TValue> : GenericSimpleObject<EnumClass>`
  - Not certain, but I don't believe the original would of handled this scenario.

### 2024-08-05 Commit [89d9a5c](https://github.com/zanders3/json/commit/89d9a5c08eb6f69d0d9efea7ae8ad3a64ddfb7df)
Converted the JSONParser & JSONWriter into a `partial static class Serialization` for better consumer side usability; everything in 1 namespace/class with minimal interface exposure. 

#### Added TinyJsonSystemExtensions.cs:
This is the new home for the TinyJson extension methods. This file is using `namespace System` so these extensions will be generally available everywhere when the DLL or `TinyJson.cs` merged file is added to a project. This includes 5 extension methods for serializing/deserializing objects to json strings or files, with or without tab indentation. Minimized interface exposure by making 2 of them overloads. Since this is the public interface for the library, XML documentation was applied to all extensions.

#### Project File Changes:
- Bumped version to 2.0.0 because at this point it is very divergent from the original.
- Added BeforeBuild target to CSPROJ that performs a 1-file merge operation.
  - The build process now has a powershell dependency. 
- Configured a Release optimized build output for DLL-only option.
- Reduced C# language version to 8 for greater compatibility.
  - Updated parser/writer code as necessary. 
- Added GitHub Action to automate building the release DLL/CS files.
- Project will now build a dll properly named `TinyJson.dll`.
  - CSPROJ attribute was added, no other project filenames were affected.

#### JSONParser.cs/JSONWriter.cs Changes:
- Moved `using` statements into `namespace` for file merge build target compatibility.
- Converted to extension methods to standard static methods and removed optional defaults.
- Reinstalled some restrictions on reading Dictionary Keys, but still restricted to string, all primitives, enum or other types with explicit simple handlers; such as decimal and datetime. This really only prevents unknown Object/Struct dictionary keys. Also note the writer now has parity with the parser functionality.

#### JSONWriter.cs Changes:
- General cleanup to abstract some of the complexity into methods.
- Implemented [PR #34](https://github.com/zanders3/json/pull/34) from original project to add indented output formatting.
- Incorporated the [RealmJoin](https://github.com/realmjoin/json) forks ability to keep serialize null values.
- Added explicit/tiny representations handlers for more built-in structs. 

**NOTE:**
After implementing PR #34 Tab Indentation by alexkrnet, the 8MB TinyJson output from the PerformanceTest() jumped up to almost 13MB with indents. After adding the RealmJoin NullPropertyInclusion mechanisms, it proved the omission of exactly 1 null property drastically (1MB) reduced the file size. Also note the Newtonsoft default indents are "spaces" and the same data (with null properties) is 21MB vs TinyJson 14M using Tabs/Null properties.

#### Test Changes:
- Added parser & writer Dictionary tests related to the new Key Type abilities.
- Updated parser `PerformanceTest()` to remove hard coded paths and use the new `TinyJsonConvert()` overload that writes the output to a file. 
  - TODO: Tests are the next thing that needs a major reorganization, the `PerformanceTest()` is a one of many examples where entirely too much is going on and there is no sharing of model stubs.
  - A second example is the `TestSimpleGenericObject()` where I went ahead and tested the a concrete implementation of a generic abstract class converts both directions as expected.
- Tested the IncludeNull behavior flag.
  - Tested various forms of nullables with and without the includeNull flag.
- Added test for the `TinyJsonTabConvert()` Tab Formatted output type.


### 2024-08-07 Commit [176555e](https://github.com/JD-Howard/TinyJson/commit/176555e4e30f8c2fedba39790cbc9fcaf9ea7cf4)
This update largely addresses any low-hanging fruit issues identified in the original projects issue tracker. This included a review of closed issues to ensure nothing was missed or improperly rejected. This does not mean 100% of the issue items have been addressed, there are always "nice to haves" or "insufficient" issues that may be too much trouble to resolve.

- Issue #50: Resolved string key escaping, double checked against Newtons output.
- Issue #49: Nullable Support, this was previously fixed, but now properly tested.
- Issue #48: Strings that were serialized as null will now parse to null.
- Issue #32: Anonymous object data loss by only returning int data type.
- Issue #29: Anonymous object strings not being un-escaped was fixed in previous update.
- Issue #13: Expanding Dictionary Key options was originally rejected by the project owner, but I technically added this functionality in the 2024/08/05 update.
- Issue #11: This was already fixed but I added tests to confirm the fix.

#### Project File Changes:
- Went ahead and finished renaming everything to actually say "TinyJson" to remove the ambiguity.

#### JSONParser.cs Changes:
- Improved performance by implementing a new strategy that uses `Trim()` to remove leading/trailing whitespace and added a `char.IsWhiteSpace() => continue` behavior to the internal character iterations. This doesn't solve the exponential performance difference with other parsers (Newtonsoft baseline) on large files, but it did change from 2/3 slower to 1/2 slower on the large file baseline I've been using.
- Bug Fix: Nullable<enum> can now properly be read as null.
- Bug Fix: Nullable<char> can now properly be read as null and consistently extracted from quotes.

#### JSONWriter.cs Changes:
- Bug Fix: Dictionary string keys are now properly un-escaped.

#### Test Changes:
Still a lot of work to do on unit testing, but I have started the process of migrating all the model stubs into their own folders/files so they can be shared between test fixtures.
- Added or expanded tests, that should fully cover parsing concrete and nullable forms of IsPrimitive, IsEnum, and is typeof(decimal).
- Expanded the string escaping tests.
- Added or expanded tests to support various Issue# resolutions; see above.
- Merged the ArrayTest & ListTest into a CollectionTest methods to be more DRY.
  - Also expanded the related tests for some edge cases.

 
