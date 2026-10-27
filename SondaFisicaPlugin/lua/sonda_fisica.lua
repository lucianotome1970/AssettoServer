-- SONDA DE FISICA DO SERVIDOR.
--
-- Este script e enviado pelo SERVIDOR (CSPServerScriptProvider) e roda no
-- cliente. Ele existe para responder tres perguntas que decidem se da para
-- fazer o push-to-pass pelo servidor, em vez do turbo postico que o carro usa
-- hoje:
--
--   1. `physics.allowed()` devolve verdadeiro num script enviado pelo
--      servidor? A documentacao do CSP diz que, se nao, "so
--      `physics.raycastTrack()` fica disponivel" -- e entao nada disso serve.
--   2. Qual a ESCALA do `physics.setCarRestrictor`? O parametro e so
--      "restrictor value", sem unidade na documentacao.
--   3. A mudanca e INSTANTANEA? Push-to-pass que demora meio segundo para
--      entrar nao serve.
--
-- NAO MEXE EM NADA SOZINHO. O restritor so muda quando o servidor manda, pelo
-- comando de administrador `/sonda`. Um script de servidor que altera a fisica
-- na chegada seria exatamente o que nos recusamos a instalar de terceiros.
--
-- A MEDIDA DA ESCALA E A ACELERACAO, nao um numero lido: nao ha getter de
-- potencia. Entao, depois de cada mudanca, o script manda rpm e velocidade a
-- cada 0,5 s por 12 s. Comparando duas passadas iguais com restritores
-- diferentes, a diferenca de aceleracao DA a escala.

local sonda = ac.connect({ ac.StructItem.key('sondaFisica') }, false, ac.SharedNamespace.ServerScript)

local Relatorio = ac.OnlineEvent({
  ac.StructItem.key('sondaRelatorio'),
  permitido = ac.StructItem.boolean(),
  versaoCSP = ac.StructItem.int32(),
  restritor = ac.StructItem.float(),
  rpm = ac.StructItem.float(),
  kmh = ac.StructItem.float(),
  nota = ac.StructItem.string(48),
})

local traco = -1          -- quanto falta de amostragem, em segundos
local restritorAtual = -1 -- -1 = nunca aplicado nesta sessao
local proximaAmostra = 0

local function contar(nota)
  local carro = ac.getCar(0)
  Relatorio{
    permitido = physics.allowed(),
    versaoCSP = ac.getPatchVersionCode(),
    restritor = restritorAtual,
    rpm = carro and carro.rpm or -1,
    kmh = carro and carro.speedKmh or -1,
    nota = nota,
  }
end

-- O servidor manda o valor; a resposta diz se a chamada passou.
local Comando = ac.OnlineEvent({
  ac.StructItem.key('sondaComando'),
  valor = ac.StructItem.float(),
  segundos = ac.StructItem.float(),
}, function(sender, dados)
  if sender ~= nil then return end        -- so aceita do SERVIDOR
  if not physics.allowed() then
    contar('NEGADO: physics.allowed = false')
    return
  end
  local ok, erro = pcall(function()
    physics.setCarRestrictor(0, dados.valor)
  end)
  restritorAtual = ok and dados.valor or -1
  contar(ok and ('aplicado ' .. tostring(dados.valor)) or ('ERRO: ' .. tostring(erro)))
  traco = dados.segundos
  proximaAmostra = 0
end)

-- Um relatorio na chegada, antes de qualquer alteracao: e ele que responde a
-- pergunta 1 sem precisar de comando nenhum.
local avisou = false

function script.update(dt)
  if not avisou then
    avisou = true
    contar('na chegada')
  end
  if traco > 0 then
    traco = traco - dt
    proximaAmostra = proximaAmostra - dt
    if proximaAmostra <= 0 then
      proximaAmostra = 0.5
      contar('traco')
    end
  end
end
