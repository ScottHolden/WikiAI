(() => {
    const askButton = document.getElementById("askbutton");
    const topSearch = document.getElementById("topSearch");
    const answerBox = document.getElementById("answerBox");
    const spinner = document.getElementById("spinner");
    const addAnswer = (question, answer, references, notes) => {
        const newAnswer = document.createElement('div');

        const newAnswerQuestion = document.createElement('h3');
        newAnswerQuestion.innerText = question;
        newAnswer.appendChild(newAnswerQuestion);

        const newAnswerClose = document.createElement('button');
        newAnswerClose.className = "close";
        newAnswerClose.innerText = "X";
        newAnswerClose.addEventListener("click", (e) => { e.target.parentElement.remove(); });
        newAnswer.appendChild(newAnswerClose);

        const newAnswerText = document.createElement('p');
        newAnswerText.innerText = answer;
        newAnswer.appendChild(newAnswerText);

        if (references) {
            const newAnswerRefs = document.createElement('div');
            for (const property in references) {
                const reference = references[property];
                const link = document.createElement('a');
                link.href = reference.url;
                link.textContent = `[${property}] ${reference.title}`;
                link.target = '_blank';
                newAnswerRefs.appendChild(link);
            }
            newAnswer.appendChild(newAnswerRefs);
        }
        if (notes) {
            const newAnswerNotes = document.createElement('div');
            newAnswerNotes.className = "small";
            newAnswerNotes.innerText = notes;
            newAnswer.appendChild(newAnswerNotes);
        }

        // Defensive update
        topSearch.className = "search";
        answerBox.prepend(newAnswer);
    };
    askButton.addEventListener("click", (e) => {
        e.preventDefault();
        const question = document.querySelector('input[type="text"]').value;
        if (!question) return;

        const searchType = document.querySelector('input[name="searchType"]:checked').value;
        topSearch.className = "search";
        spinner.style.display = "block";
        fetch(`/api/ask?engine=${searchType}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                question: question
            })
        })
            .then(response => response.json())
            .then(data => {
                spinner.style.display = "none";
                console.log(data);
                addAnswer(question, data.answer, data.references, data.notes);
            })
            .catch(error => {
                console.error(error);
                spinner.style.display = "none";
                addAnswer(question, "Couldn't connect to the server, please try again.");
            });
    });
    document.getElementById("clearbutton").addEventListener("click", (e) => {
        answerBox.innerHTML = "";
        topSearch.className = "search new";
    });
})();