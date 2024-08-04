# Tiny Json
This will be a non-trivial revision to the original project, and I've incorporated some of the notable additions other forks have added over the years. Please refer to the [original project](https://github.com/zanders3/json) use case documentation, I will only be documentation notable changes from the original project.

### TLDR:
TinyJson is a great option if you:
- Don't want additional dependencies
  - Supply chain attacks were definitively one of my motivations.
  - this is configured for NetStandard2.0 targeting, but if your importing this code directly into your project, then you may need to upgrade your csproj LangVersion.
- Don't have overly complex JSON needs
- Need to work with lots of small JSON chunks.
  - Meets or beats Newtonsoft performance for small items.
- Don't need to work with large JSON files.
  - 2/3 slower than Newtonsoft on fairly nested 8MB file.

## Change Log
For me, the intent of this project is to internalize these files into another project that needs basic JSON support. So, this change log will not focus on versions and simply document what each of my commits accomplished.

### 2024-08-04 Commit [7b16d4b](https://github.com/JD-Howard/TinyJson/commit/7b16d4bd41c884a86de554adec19d0c196e0106e)
#### Project File Changes:
- Changed the json project to target NetStandard2.0 so it will largely work everywhere.
- Changed the json project CSPROJ to use the latest C# language features.
- Changed the test project to target net8 so I could use the latest MSTest nuget packages.
- Added `.editorconfig` to preserve the original unix line endings.
- Added `nuget.config` to keep the packages I do use local to my project folder.
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
It has become clear that this will never perform at the level it should while using a StringBuilder. In order to fix this, a single-pass char[] strategy closer to the way SimpleJson parses would need to be deployed. I will continue to get this where I need it for my immediate netstandard needs, but *it would be ill-advised to use TinyJson on truly large json files;* such as +50MB. Also, I may in fact fork/migrate/update SimpleJson too given this large implementation limitation.


#### JSONWriter.cs Changes:
- Changed the DateTime to JSON format to match ISO 8601; which preserves timezone information.
- Added a TimeSpan handler so it will stop creating excessively large representations.

**NOTE:**
This writer tends to entirely omit null values rather than including them in the output JSON. I don't think this is a big problem, it makes smaller json,  but it is a little odd to see {} representing an entire object, and this may be an undesirable for some users or even a possible problem in some situations.

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
