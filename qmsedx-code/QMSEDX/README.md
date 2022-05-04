QMSEDX 12.50.0 © 2020 CWolf
License: GPLv3
  * Free for private and commercial use!
  * No liability, no warranty!

The program is used to execute a single EDX task in a QlikView Management Console and wait for the result(s).

**Supported QlikView Versions: 12.50 and higher**

**Installation:**

Copy all Files from zip to a local folder.

**Configuration:**

If QMS server is not localhost, you have to set it in file "QMSEDX.exe.config" in the following line:
~~~
<endpoint address="http://localhost:4799/QMS/Service"... />
~~~
Change "localhost" with the name of your QMS server.

**Usage:**

- **Tasks list**: (Membership in the groups "QlikView Management API" and "QlikView EDX" on the QMS server is necessary!)
  qmsedx.exe -list
   	List all EDX Tasks on QMS

- **Task execution**: (Membership in the group "QlikView EDX" on the QMS server is necessary!)
  qmsedx.exe -task="task name" [-password=password] [-variablename=vname] [-variablevalues=vvalues] [-timeout=seconds] [-pollinterval=seconds] [-delay=seconds] [-nowait] [-verbosity=level]

  *Arguments:*
  -task [REQUIRED]: The name or id of the task to execute.
  -password (-pwd) [OPTIONAL]: The password required to execute the task, if set.
  -variablename (-vn) [OPTIONAL]: The name of the variable to pass on to the task, if set.
  -variablevalues (-vv) [OPTIONAL]: A semicolon separated list of values for the variable to pass on to the task.
  -timeout (-to) [OPTIONAL]: How many seconds to wait for a single task to finish. Default value is 3600 (1 hours).
  -pollinterval (-pi) [OPTIONAL]: How often to check the status of the task. Default value is every five seconds.
  -delay [OPTIONAL]: How many seconds to delay for the program to terminate. Default is 0.
  -nowait (-nw) [OPTIONAL]: Don't wait for task completion.
  -verbosity (-verb) [OPTIONAL]: The level of output, 0-4. 0 will not produce any output and 4 (default) is the most verbose.