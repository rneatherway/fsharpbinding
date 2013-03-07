;;; fsharp-mode-completion.el --- Autocompletion support for F#

;; Copyright (C) 2012-2013 Robin Neatherway

;; Author: Robin Neatherway <robin.neatherway@gmail.com>
;; Maintainer: Robin Neatherway <robin.neatherway@gmail.com>
;; Keywords: languages

;; This file is not part of GNU Emacs.

;; This file is free software; you can redistribute it and/or modify
;; it under the terms of the GNU General Public License as published by
;; the Free Software Foundation; either version 3, or (at your option)
;; any later version.

;; This file is distributed in the hope that it will be useful,
;; but WITHOUT ANY WARRANTY; without even the implied warranty of
;; MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
;; GNU General Public License for more details.

;; You should have received a copy of the GNU General Public License
;; along with GNU Emacs; see the file COPYING.  If not, write to
;; the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
;; Boston, MA 02110-1301, USA.

(require 'namespaces)

(namespace fsharp-mode-completion
  :export
  [load-project
   start-process
   stop-process
   show-tooltip-at-point
   show-typesig-at-point
   show-error-at-point
   next-error
   fsharp-overlay-at]
  :use
  [(popup popup-tip)
   (pos-tip pos-tip-show)
   dash
   s
   (fsharp-doc fsharp-doc/format-for-minibuffer)
   (fsharp-mode-indent fsharp-in-literal-p)])

;;; User-configurable variables

(defvar ac-fsharp-executable "fsautocomplete.exe")

(defvar ac-fsharp-complete-command
  (let ((exe (or (executable-find ac-fsharp-executable)
                 (concat (file-name-directory (or load-file-name buffer-file-name))
                         "bin/" ac-fsharp-executable))))
    (case system-type
      (windows-nt exe)
      (otherwise (list "mono" exe)))))

(defvar ac-fsharp-use-popup t
  "Display tooltips using a popup at point. If set to nil,
display in a help buffer instead.")

(defface fsharp-error-face
  '((t :inherit error))
  "Face used for marking an error in F#"
  :group 'fsharp)

(defface fsharp-warning-face
  '((t :inherit warning))
  "Face used for marking a warning in F#"
  :group 'fsharp)

;;; Both in seconds. Note that background process uses ms.
(defvar ac-fsharp-blocking-timeout 1)
(defvar ac-fsharp-idle-timeout 1)

;;; ----------------------------------------------------------------------------

(defvar ac-fsharp-status 'idle)
(defvar ac-fsharp-completion-process nil)
(defvar ac-fsharp-partial-data "")
(defvar ac-fsharp-completion-data "")
(defvar ac-fsharp-completion-cache nil)
(defvar ac-fsharp-project-files nil)
(defvar ac-fsharp-idle-timer nil)
(defvar ac-fsharp-verbose nil)
(defvar ac-fsharp-waiting nil)

(defun log-to-proc-buf (proc str)
  (when (processp proc)
    (let ((buf (process-buffer proc))
          (atend (with-current-buffer (process-buffer proc)
                   (eq (marker-position (process-mark proc)) (point)))))
      (when (buffer-live-p buf)
        (with-current-buffer buf
          (goto-char (process-mark proc))
          (insert-before-markers str))
        (if atend
            (with-current-buffer buf
              (goto-char (process-mark proc))))))))

(defun log-psendstr (proc str)
  (log-to-proc-buf proc str)
  (process-send-string proc str))

(defun ac-fsharp-parse-current-buffer ()
  (save-restriction
    (widen)
    (process-send-string
     ac-fsharp-completion-process
     (format "parse \"%s\" full\n%s\n<<EOF>>\n"
             (buffer-file-name)
             (buffer-substring-no-properties (point-min) (point-max))))))

(defun ac-fsharp-parse-file (file)
  (with-current-buffer (find-file-noselect file)
    (ac-fsharp-parse-current-buffer)))

;;;###autoload
(defn load-project (file)
  "Load the specified F# file as a project"
  (assert (equal "fsproj" (file-name-extension file))  ()
          "The given file was not an F# project.")

  ;; Prompt user for an fsproj, searching for a default.
  (interactive
   (list (read-file-name
          "Path to project: "
          (fsharp-mode/find-fsproj buffer-file-name)
          (fsharp-mode/find-fsproj buffer-file-name))))

  ;; Reset state.
  (setq ac-fsharp-completion-cache nil
        ac-fsharp-partial-data nil
        ac-fsharp-project-files nil)

  ;; Launch the completion process and update the current project.
  (let ((f (expand-file-name file)))
    (unless ac-fsharp-completion-process
      (_ start-process))
    (log-psendstr ac-fsharp-completion-process
                  (format "project \"%s\"\n" (expand-file-name file)))))

(defun ac-fsharp-send-pos-request (cmd file line col)
  (let ((request (format "%s \"%s\" %d %d %d\n" cmd file line col
                         (* 1000 ac-fsharp-blocking-timeout))))
      (log-psendstr ac-fsharp-completion-process request)))

;;;###autoload
(defn stop-process ()
  (interactive)
  (_ message-safely "Quitting fsharp completion process")
  (when
      (and ac-fsharp-completion-process
           (process-live-p ac-fsharp-completion-process))
    (log-psendstr ac-fsharp-completion-process "quit\n")
    (sleep-for 1)
    (when (process-live-p ac-fsharp-completion-process)
      (kill-process ac-fsharp-completion-process)))
  (when ac-fsharp-idle-timer
    (cancel-timer ac-fsharp-idle-timer))
  (setq ac-fsharp-status 'idle
        ac-fsharp-completion-process nil
        ac-fsharp-partial-data ""
        ac-fsharp-completion-data ""
        ac-fsharp-completion-cache nil
        ac-fsharp-project-files nil
        ac-fsharp-idle-timer nil
        ac-fsharp-verbose nil
        ac-fsharp-waiting nil)
  (_ clear-errors))

;;;###autoload
(defn start-process ()
  "Launch the F# completion process in the background"
  (interactive)
  (if ac-fsharp-completion-process
      (_ message-safely "Completion process already running. Shutdown existing process first.")
    (_ message-safely (format "Launching completion process: '%s'" (s-join " " ac-fsharp-complete-command)))
    (setq ac-fsharp-completion-process
          (let ((process-connection-type nil))
            (apply 'start-process
                   "fsharp-complete"
                   "*fsharp-complete*"
                   ac-fsharp-complete-command)))

    (if (process-live-p ac-fsharp-completion-process)
        (progn
          (set-process-filter ac-fsharp-completion-process (~ filter-output))
          (set-process-query-on-exit-flag ac-fsharp-completion-process nil)
          (setq ac-fsharp-status 'idle)
          (setq ac-fsharp-partial-data "")
          (setq ac-fsharp-project-files))
      (setq ac-fsharp-completion-process nil))

    (setq ac-fsharp-idle-timer
          (run-with-idle-timer ac-fsharp-idle-timeout t (~ request-errors)))))

; Consider using 'text' for filtering
; TODO: This caching is a bit optimistic. It might not always be correct
;       to use the cached values if the line and col just happen to line up.
;       Could dirty cache on idle, or include timestamps and ignore values
;       older than a few seconds. On the other hand it only caches the most
;       recent position, so it's very unlikely to try that position again
;       without the completions being the same unless another completion has
;       been tried in between.
(defun ac-fsharp-completions (file line col text)
  (setq ac-fsharp-waiting t)
  (let ((cache (assoc file ac-fsharp-completion-cache)))
    (if (and cache (equal (cddr cache) (list line col)))
        (cadr cache)
      (ac-fsharp-parse-current-buffer)
      (ac-fsharp-send-pos-request "completion" file line col)
      (while ac-fsharp-waiting
        (accept-process-output ac-fsharp-completion-process))
      (when ac-fsharp-completion-data
        (push (list file ac-fsharp-completion-data line col) ac-fsharp-completion-cache))
      ac-fsharp-completion-data)))

(defun ac-fsharp-completion-at-point ()
  "Return a function ready to interrogate the F# compiler service for completions at point."
  (if ac-fsharp-completion-process
      (let ((end (point))
            (start
             (save-excursion
               (skip-chars-backward "^ ." (line-beginning-position))
               (point))))
        (list start end
              (completion-table-dynamic
               (apply-partially #'ac-fsharp-completions
                                (buffer-file-name)
                                (- (line-number-at-pos) 1)
                                (current-column)))))
  ; else
    nil))

(defun ac-fsharp-can-make-request ()
  (and ac-fsharp-completion-process
       (or
        (member (expand-file-name (buffer-file-name)) ac-fsharp-project-files)
        (string-match-p "\\(fsx\\|fsscript\\)" (file-name-extension (buffer-file-name))))))

(defmutable awaiting-tooltip nil)

;;;###autoload
(defn show-tooltip-at-point ()
  "Display a tooltip for the F# symbol at POINT."
  (interactive)
  (@set awaiting-tooltip t)
  (_ show-typesig-at-point))

;;;###autoload
(defn show-typesig-at-point ()
  "Display the type signature for the F# symbol at POINT."
  (interactive)
  (when (ac-fsharp-can-make-request)
    (ac-fsharp-parse-current-buffer)
    (ac-fsharp-send-pos-request "tooltip"
                                (buffer-file-name)
                                (- (line-number-at-pos) 1)
                                (current-column))))

;;;###autoload
(defun ac-fsharp-gotodefn-at-point ()
  "Find the point of declaration of the symbol at point and goto it"
  (interactive)
  (when (ac-fsharp-can-make-request)
    (ac-fsharp-parse-current-buffer)
    (ac-fsharp-send-pos-request "finddecl"
                                (buffer-file-name)
                                (- (line-number-at-pos) 1)
                                (current-column))))

(defun ac-fsharp-electric-dot ()
  (interactive)
  (insert ".")
  (unless (fsharp-in-literal-p)
    (completion-at-point)))

;;; ----------------------------------------------------------------------------
;;; Errors and Overlays

(defstruct fsharp-error start end face text)

(defmutable errors)
(make-local-variable (~ errors))

(def error-regexp
     "\\[\\([0-9]+\\):\\([0-9]+\\)-\\([0-9]+\\):\\([0-9]+\\)\\] \\(ERROR\\|WARNING\\) \\(.*\\(?:\n[^[].*\\)*\\)"
     "Regexp to match errors that come from fsautocomplete. Each
starts with a character range for position and is followed by
possibly many lines of description.")

(defn request-errors ()
  (when (ac-fsharp-can-make-request)
    (ac-fsharp-parse-current-buffer)
    (log-psendstr ac-fsharp-completion-process "errors\n")))

(defn line-column-to-pos (line col)
  (save-excursion
    (goto-char (point-min))
    (forward-line (- line 1))
    (if (< (point-max) (+ (point) col))
        (point-max)
      (forward-char col)
      (point))))

(defn parse-errors (str)
  "Extract the errors from the given process response. Returns a list of fsharp-error."
  (save-match-data
    (let (parsed)
      (while (string-match (@ error-regexp) str)
        (let ((beg (_ line-column-to-pos (+ (string-to-number (match-string 1 str)) 1)
                      (string-to-number (match-string 2 str))))
              (end (_ line-column-to-pos (+ (string-to-number (match-string 3 str)) 1)
                      (string-to-number (match-string 4 str))))
              (face (if (string= "ERROR" (match-string 5 str))
                        'fsharp-error-face
                      'fsharp-warning-face))
              (msg (match-string 6 str))
              )
          (setq str (substring str (match-end 0)))
          (add-to-list 'parsed (make-fsharp-error :start beg
                                                  :end   end
                                                  :face  face
                                                  :text  msg))))
      parsed)))

(defn show-error-overlay (err)
  "Draw overlays in the current buffer to represent fsharp-error ERR."
  ;; Three cases
  ;; 1. No overlays here yet: make it
  ;; 2. new warning, exists error: do nothing
  ;; 3. new error exists warning: rm warning and make it
  (let* ((beg  (fsharp-error-start err))
         (end  (fsharp-error-end err))
         (face (fsharp-error-face err))
         (txt  (fsharp-error-text err))
         (ofaces (mapcar (lambda (o) (overlay-get o 'face))
                         (overlays-in beg end)))
         )
    (unless (and (eq face 'fsharp-warning-face)
                 (memq 'fsharp-error-face ofaces))

      (when (and (eq face 'fsharp-error-face)
                 (memq 'fsharp-warning-face ofaces))
        (remove-overlays beg end 'face 'fsharp-warning-face))

      (let ((ov (make-overlay beg end)))
        (overlay-put ov 'face face)
        (overlay-put ov 'help-echo txt)))))

(defn clear-errors ()
  (interactive)
  (remove-overlays nil nil 'face 'fsharp-error-face)
  (remove-overlays nil nil 'face 'fsharp-warning-face)
  (@set errors nil))

;;; ----------------------------------------------------------------------------
;;; Error navigation
;;;
;;; These functions hook into Emacs' error navigation API and should not
;;; be called directly by users.

(defn message-safely (format-string &rest args)
  "Calls MESSAGE only if it is desirable to do so."
  (when (equal major-mode 'fsharp-mode)
    (unless (or (active-minibuffer-window) cursor-in-echo-area)
      (apply 'message format-string args))))

(defn error-position (n-steps errs)
  "Calculate the position of the next error to move to."
  (let* ((xs (->> (sort (-map 'fsharp-error-start errs) '<)
               (--remove (= (point) it))
               (--split-with (>= (point) it))))
         (before (nreverse (car xs)))
         (after  (cadr xs))
         (errs   (if (< n-steps 0) before after))
         (step   (- (abs n-steps) 1))
         )
    (nth step errs)))

(defn next-error (n-steps reset)
  "Move forward N-STEPS number of errors, possibly wrapping
around to the start of the buffer."
  (when reset
    (goto-char (point-min)))

  (let ((pos (_ error-position n-steps (@ errors))))
    (if pos
        (goto-char pos)
      (error "No more F# errors"))))

(defn fsharp-overlay-p (ov)
  (let ((face (overlay-get ov 'face)))
    (or (equal 'fsharp-warning-face face)
        (equal 'fsharp-error-face face))))

(defn fsharp-overlay-at (pos)
  (car-safe (-filter (~ fsharp-overlay-p)
                     (overlays-at pos))))

;;; HACK: show-error-at point checks last position of point to prevent
;;; corner-case interaction issues, e.g. when running `describe-key`
(defmutable last-point)

(defn show-error-at-point ()
  (let ((ov (_ fsharp-overlay-at (point)))
        (changed-pos (not (equal (point) (@ last-point)))))
    (@set last-point (point))

    (when (and ov changed-pos)
      (_ message-safely (overlay-get ov 'help-echo)))))

;;; ----------------------------------------------------------------------------
;;; Process handling
;;; Handle output from the completion process.

(def eom "\n<<EOF>>\n")

(defn filter-output (proc str)
  "Filter output from the completion process and handle appropriately."
  (log-to-proc-buf proc str)
  (setq ac-fsharp-partial-data (concat ac-fsharp-partial-data str))

  (let ((eofloc (string-match-p (@ eom) ac-fsharp-partial-data)))
    (while eofloc
      (let ((msg  (substring ac-fsharp-partial-data 0 eofloc))
            (part (substring ac-fsharp-partial-data (+ eofloc (length (@ eom))))))
        (cond
         ((s-starts-with? "DATA: completion" msg) (_ set-completion-data msg))
         ((s-starts-with? "DATA: finddecl" msg)   (_ visit-definition msg))
         ((s-starts-with? "DATA: tooltip" msg)    (_ handle-tooltip msg))
         ((s-starts-with? "DATA: errors" msg)     (_ handle-errors msg))
         ((s-starts-with? "DATA: project" msg)    (_ handle-project msg))
         ((s-starts-with? "ERROR: " msg)          (_ handle-process-error msg))
         ((s-starts-with? "INFO: " msg) (when ac-fsharp-verbose (_ message-safely msg)))
         (t
          (_ message-safely "Error: unrecognised message: '%s'" msg)))

        (setq ac-fsharp-partial-data part))
      (setq eofloc (string-match-p (@ eom) ac-fsharp-partial-data)))))

(defn set-completion-data (str)
  (setq ac-fsharp-completion-data (s-split "\n" (s-replace "DATA: completion" "" str) t)
        ac-fsharp-waiting nil))

(defn visit-definition (str)
  (if (string-match "\n\\(.*\\):\\([0-9]+\\):\\([0-9]+\\)" str)
      (let ((file (match-string 1 str))
            (line (+ 1 (string-to-number (match-string 2 str))))
            (col (string-to-number (match-string 3 str))))
        (find-file (match-string 1 str))
        (goto-char (_ line-column-to-pos line col)))
    (_ message-safely "Unable to find definition.")))

(defn handle-errors (str)
  "Display error overlays and set buffer-local error variables for error navigation."
  (_ clear-errors)
  (let ((errs (_ parse-errors
                 (concat (replace-regexp-in-string "DATA: errors\n" "" str) "\n")))
        )
    (@set errors errs)
    (mapc (~ show-error-overlay) errs)))

(defn handle-tooltip (str)
  "Display information from the background process. If the user
has requested a popup tooltip, display a popup. Otherwise,
display a short summary in the minibuffer."
  ;; Do not display if the current buffer is not an fsharp buffer.
  (when (equal major-mode 'fsharp-mode)
    (unless (or (active-minibuffer-window) cursor-in-echo-area)
      (let ((cleaned (replace-regexp-in-string "DATA: tooltip\n" "" str)))
        (if (@ awaiting-tooltip)
            (progn
              (@set awaiting-tooltip nil)
              (if ac-fsharp-use-popup
                  (_ show-popup cleaned)
                (_ show-info-window cleaned)))
          (_ message-safely (fsharp-doc/format-for-minibuffer cleaned)))))))

(defn show-popup (str)
  (if (display-graphic-p)
      (pos-tip-show str)
    ;; Use unoptimized calculation for popup, making it less likely to
    ;; wrap lines.
    (let ((popup-use-optimized-column-computation nil) )
      (popup-tip str))))

(def info-buffer-name "*fsharp info*")

(defn show-info-window (str)
  (save-excursion
    (let ((help-window-select t))
      (with-help-window (@ info-buffer-name)
        (princ str)))))

(defn handle-project (str)
  (setq ac-fsharp-project-files (cdr (split-string str "\n")))
  (ac-fsharp-parse-file (car (last ac-fsharp-project-files))))

(defn handle-process-error (str)
  (unless (s-matches? "Could not get type information" str)
    (_ message-safely str))
  (when ac-fsharp-waiting
    (setq ac-fsharp-completion-data nil)
    (setq ac-fsharp-waiting nil)))

;;; fsharp-mode-completion.el ends here
