using Microsoft.VisualStudio.TestTools.UnitTesting;

// Os testes de integração compartilham o SQL Server real e APIs isoladas, mas
// alguns cenários alteram estado de conta e sessão. A execução serial evita
// interferência entre fixtures e mantém os limites de login determinísticos.
[assembly: DoNotParallelize]
